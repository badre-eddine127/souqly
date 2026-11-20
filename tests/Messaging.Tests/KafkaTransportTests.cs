using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Souqly.Messaging;
using Souqly.Testing;

namespace Messaging.Tests;

/// <summary>
/// Aller-retour sur un vrai Kafka : outbox → producteur → topic → groupe de consommateurs → gestionnaire,
/// avec les en-têtes (identifiant, type, traceparent) et la DLQ. Exécuté dans la CI (Docker).
/// </summary>
[Trait("Category", "Kafka")]
public sealed class KafkaTransportTests : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private readonly KafkaFixture _kafka = new();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.InitializeAsync(), _kafka.InitializeAsync());
        _host = TestPlatform.Build(_postgres.ConnectionString, settings: new Dictionary<string, string?>
        {
            ["Messaging:Transport"] = "Kafka",
            ["ConnectionStrings:Kafka"] = _kafka.BootstrapServers,
            ["Messaging:OutboxPollInterval"] = "00:00:00.100",
            ["Messaging:RetryBaseDelay"] = "00:00:00.050",
        });
        await _host.EnsureDatabaseAsync();
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
        await Task.WhenAll(_postgres.DisposeAsync(), _kafka.DisposeAsync());
    }

    [Fact]
    public async Task Message_ecrit_dans_l_outbox_lu_par_le_consommateur_avec_ses_en_tetes()
    {
        var note = new NoteWritten(Guid.NewGuid(), "via kafka");
        Guid messageId;
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
            scope.ServiceProvider.GetRequiredService<IEventPublisher>().Publish(TestPlatform.Topic, "commande-42", note);
            messageId = db.ChangeTracker.Entries<OutboxMessage>().Single().Entity.Id;
            await db.SaveChangesAsync();
        }

        var calls = _host.Services.GetRequiredService<Calls>();
        await WaitAsync(() => calls.Items.Any(c => c.Message.NoteId == note.NoteId));
        var call = calls.Items.First(c => c.Message.NoteId == note.NoteId);
        call.Message.ShouldBe(note);
        call.Context.MessageId.ShouldBe(messageId);
        call.Context.Key.ShouldBe("commande-42");
        call.Context.Type.ShouldBe(nameof(NoteWritten));
        call.Context.Topic.ShouldBe(TestPlatform.Topic);
    }

    [Fact]
    public async Task Message_en_echec_publie_dans_le_topic_DLQ()
    {
        var note = new NoteWritten(Guid.NewGuid(), "boom");
        await using (var scope = _host.Services.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IEventPublisher>().Publish(TestPlatform.Topic, "k", note);
            await scope.ServiceProvider.GetRequiredService<TestDbContext>().SaveChangesAsync();
        }

        await WaitAsync(() => _host.Services.GetRequiredService<Calls>().Items.Count(c => c.Message.NoteId == note.NoteId) >= 3);

        using var consumer = new Confluent.Kafka.ConsumerBuilder<string, string>(new Confluent.Kafka.ConsumerConfig
        {
            BootstrapServers = _kafka.BootstrapServers,
            GroupId = "dlq-reader-" + Guid.NewGuid(),
            AutoOffsetReset = Confluent.Kafka.AutoOffsetReset.Earliest,
        }).Build();
        consumer.Subscribe(Topics.DeadLetter(TestPlatform.Topic));
        var dead = consumer.Consume(TimeSpan.FromSeconds(20));
        dead.ShouldNotBeNull();
        dead.Message.Value.ShouldContain(note.NoteId.ToString());
    }

    private static async Task WaitAsync(Func<bool> probe)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (probe())
            {
                return;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("Message non reçu à temps.");
    }
}
