using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Souqly.Messaging;
using Souqly.Testing;

namespace Messaging.Tests;

/// <summary>Garanties de la messagerie, vérifiées sur un vrai PostgreSQL (transactions, verrous, contraintes).</summary>
[Trait("Category", "Integration")]
public sealed class OutboxInboxTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private readonly FlakyTransport _transport = new();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        _host = TestPlatform.Build(_postgres.ConnectionString, s => s.UseTransport(_transport));
        await _host.EnsureDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        _host.Dispose();
        await _postgres.DisposeAsync();
    }

    private OutboxPublisher<TestDbContext> Publisher => _host.Services.GetServices<IHostedService>().OfType<OutboxPublisher<TestDbContext>>().Single();

    private IMessageDispatcher Dispatcher => _host.Services.GetRequiredService<IMessageDispatcher>();

    private async Task<T> WithDbAsync<T>(Func<TestDbContext, IServiceProvider, Task<T>> work)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider.GetRequiredService<TestDbContext>(), scope.ServiceProvider);
    }

    [Fact]
    public async Task L_evenement_n_est_publie_que_si_la_modification_metier_est_validee()
    {
        await WithDbAsync(async (db, sp) =>
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            db.Notes.Add(new Note { Id = Guid.NewGuid(), Text = "annulée" });
            sp.GetRequiredService<IEventPublisher>().Publish(TestPlatform.Topic, "k1", new NoteWritten(Guid.NewGuid(), "annulée"));
            await db.SaveChangesAsync();
            await tx.RollbackAsync();   // l'écriture métier échoue : l'événement doit disparaître avec elle
            return 0;
        });
        await WithDbAsync(async (db, sp) =>
        {
            sp.GetRequiredService<IEventPublisher>().Publish(TestPlatform.Topic, "k2", new NoteWritten(Guid.NewGuid(), "validée"));
            await db.SaveChangesAsync();
            return 0;
        });

        (await Publisher.PublishBatchAsync(CancellationToken.None)).ShouldBe(1);
        _transport.Sent.Select(s => s.Message.Key).ShouldBe(["k2"]);
    }

    [Fact]
    public async Task Broker_en_panne_le_message_reste_en_base_puis_part_au_retour_dans_l_ordre()
    {
        await WithDbAsync(async (db, sp) =>
        {
            var events = sp.GetRequiredService<IEventPublisher>();
            for (var i = 1; i <= 3; i++)
            {
                events.Publish(TestPlatform.Topic, "commande-1", new NoteWritten(Guid.NewGuid(), $"step {i}"));
                await Task.Delay(2);   // horodatages distincts
            }

            await db.SaveChangesAsync();
            return 0;
        });

        _transport.Down = true;
        (await Publisher.PublishBatchAsync(CancellationToken.None)).ShouldBe(0);
        var pending = await WithDbAsync((db, _) => db.Set<OutboxMessage>().Where(m => m.PublishedAt == null).ToListAsync());
        pending.Count.ShouldBe(3);
        pending.Single(m => m.Attempts == 1).LastError.ShouldBe("broker indisponible");   // arrêt au premier échec : l'ordre est préservé

        _transport.Down = false;
        (await Publisher.PublishBatchAsync(CancellationToken.None)).ShouldBe(3);
        _transport.Sent.Select(s => System.Text.Json.JsonDocument.Parse(s.Message.Payload).RootElement.GetProperty("text").GetString())
            .ShouldBe(["step 1", "step 2", "step 3"]);
    }

    [Fact]
    public async Task Un_message_recu_deux_fois_n_est_traite_qu_une_fois()
    {
        var message = MessageEnvelope.Create("k", new NoteWritten(Guid.NewGuid(), "unique"), DateTimeOffset.UtcNow);

        (await Dispatcher.DispatchAsync(TestPlatform.Topic, message, CancellationToken.None)).ShouldBe(DispatchOutcome.Handled);
        (await Dispatcher.DispatchAsync(TestPlatform.Topic, message, CancellationToken.None)).ShouldBe(DispatchOutcome.Duplicate);

        _host.Services.GetRequiredService<Calls>().Items.Count.ShouldBe(1);
        (await WithDbAsync((db, _) => db.Notes.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Dix_livraisons_simultanees_du_meme_message_un_seul_effet()
    {
        var message = MessageEnvelope.Create("k", new NoteWritten(Guid.NewGuid(), "concurrent"), DateTimeOffset.UtcNow);

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(async _ =>
        {
            try
            {
                return await Dispatcher.DispatchAsync(TestPlatform.Topic, message, CancellationToken.None);
            }
            catch (DbUpdateException)
            {
                return DispatchOutcome.Duplicate;   // la clé primaire de la note a protégé ; l'inbox fera le reste au nouvel essai
            }
        }));

        outcomes.Count(o => o == DispatchOutcome.Handled).ShouldBe(1);
        (await WithDbAsync((db, _) => db.Notes.CountAsync())).ShouldBe(1);
        (await WithDbAsync((db, _) => db.Set<InboxMessage>().CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Gestionnaire_en_echec_rien_n_est_enregistre_et_le_message_peut_etre_rejoue()
    {
        var message = MessageEnvelope.Create("k", new NoteWritten(Guid.NewGuid(), "boom"), DateTimeOffset.UtcNow);

        await Should.ThrowAsync<InvalidOperationException>(() => Dispatcher.DispatchAsync(TestPlatform.Topic, message, CancellationToken.None));

        (await WithDbAsync((db, _) => db.Notes.CountAsync())).ShouldBe(0);   // la note écrite avant l'erreur est annulée
        (await WithDbAsync((db, _) => db.Set<InboxMessage>().CountAsync())).ShouldBe(0);   // pas marqué « traité » : il sera rejoué
    }

    [Fact]
    public async Task Type_de_message_inconnu_ignore_sans_erreur()
    {
        var other = new MessageEnvelope(Guid.NewGuid(), "SomethingElse", "k", DateTimeOffset.UtcNow, "{}", null);
        (await Dispatcher.DispatchAsync(TestPlatform.Topic, other, CancellationToken.None)).ShouldBe(DispatchOutcome.Ignored);
    }
}
