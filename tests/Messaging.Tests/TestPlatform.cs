using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Souqly.Messaging;

namespace Messaging.Tests;

/// <summary>Entité métier minimale pour tester l'outbox et l'inbox.</summary>
public sealed class Note
{
    public Guid Id { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class TestDbContext(DbContextOptions<TestDbContext> options) : DbContext(options)
{
    public DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.AddMessagingTables();
        modelBuilder.Entity<Note>().ToTable("notes");
    }
}

public sealed record NoteWritten(Guid NoteId, string Text);

/// <summary>Gestionnaire de test : enregistre une note, et échoue si le texte vaut « boom ».</summary>
public sealed class NoteWrittenHandler(TestDbContext db, Calls calls) : IMessageHandler<NoteWritten>
{
    public async Task HandleAsync(NoteWritten message, MessageContext context, CancellationToken ct)
    {
        calls.Add(message, context);
        db.Notes.Add(new Note { Id = message.NoteId, Text = message.Text });
        await db.SaveChangesAsync(ct);
        if (message.Text == "boom")
        {
            throw new InvalidOperationException("panne simulée après écriture");
        }
    }
}

public sealed class Calls
{
    private readonly ConcurrentQueue<(NoteWritten Message, MessageContext Context)> _items = new();

    public IReadOnlyList<(NoteWritten Message, MessageContext Context)> Items => _items.ToArray();

    public void Add(NoteWritten message, MessageContext context) => _items.Enqueue((message, context));
}

/// <summary>Transport qu'on peut mettre « en panne » pour simuler un broker indisponible.</summary>
public sealed class FlakyTransport : IMessageTransport
{
    private readonly ConcurrentQueue<(string Topic, MessageEnvelope Message)> _sent = new();

    public bool Down { get; set; }

    public IReadOnlyList<(string Topic, MessageEnvelope Message)> Sent => _sent.ToArray();

    public Task PublishAsync(string topic, MessageEnvelope message, CancellationToken ct)
    {
        if (Down)
        {
            throw new InvalidOperationException("broker indisponible");
        }

        _sent.Enqueue((topic, message));
        return Task.CompletedTask;
    }
}

internal static class TestPlatform
{
    public const string Topic = "souqly.test.notes.v1";

    /// <summary>Hôte de test avec la messagerie, sans démarrer les tâches de fond (on les pilote à la main).</summary>
    public static IHost Build(string connectionString, Action<IServiceCollection>? services = null, Dictionary<string, string?>? settings = null)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { EnvironmentName = "Testing", ApplicationName = "Souqly.Notes.Api" });
        builder.Configuration.AddInMemoryCollection(settings ?? new Dictionary<string, string?> { ["Messaging:Transport"] = "InMemory" });
        builder.Services.AddDbContext<TestDbContext>(o => o.UseNpgsql(connectionString).UseSnakeCaseNamingConvention());
        builder.Services.AddSingleton<Calls>();
        builder.AddSouqlyMessaging<TestDbContext>(m => m.Consume<NoteWritten, NoteWrittenHandler>(Topic));
        services?.Invoke(builder.Services);
        return builder.Build();
    }

    public static IServiceCollection UseTransport(this IServiceCollection services, IMessageTransport transport)
    {
        services.RemoveAll<IMessageTransport>();
        services.AddSingleton(transport);
        return services;
    }

    public static async Task EnsureDatabaseAsync(this IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<TestDbContext>().Database.EnsureCreatedAsync();
    }
}
