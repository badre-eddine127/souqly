using Microsoft.EntityFrameworkCore;

namespace Souqly.Messaging;

/// <summary>Message en attente d'envoi (outbox transactionnelle).</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public string? TraceParent { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    internal static OutboxMessage From(string topic, MessageEnvelope e) => new()
    {
        Id = e.Id,
        Topic = topic,
        Key = e.Key,
        Type = e.Type,
        Payload = e.Payload,
        TraceParent = e.TraceParent,
        OccurredAt = e.OccurredAt,
    };

    internal MessageEnvelope ToEnvelope() => new(Id, Type, Key, OccurredAt, Payload, TraceParent);
}

/// <summary>Message déjà traité par un consommateur (inbox) : garantit un effet unique malgré les relivraisons.</summary>
public sealed class InboxMessage
{
    public Guid MessageId { get; set; }
    public string Consumer { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public DateTimeOffset ProcessedAt { get; set; }
}

public static class MessagingModelBuilderExtensions
{
    /// <summary>Ajoute les tables outbox_messages et inbox_messages au modèle du service.</summary>
    public static ModelBuilder AddMessagingTables(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(b =>
        {
            b.ToTable("outbox_messages");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Topic).HasMaxLength(200).IsRequired();
            b.Property(m => m.Key).HasMaxLength(100).IsRequired();
            b.Property(m => m.Type).HasMaxLength(100).IsRequired();
            b.Property(m => m.Payload).HasColumnType("jsonb").IsRequired();
            b.Property(m => m.TraceParent).HasMaxLength(64);
            b.Property(m => m.LastError).HasMaxLength(1000);
            // Index partiel : le publieur ne lit que les messages non envoyés, dans l'ordre.
            b.HasIndex(m => m.OccurredAt).HasFilter("published_at IS NULL").HasDatabaseName("ix_outbox_messages_pending");
        });

        modelBuilder.Entity<InboxMessage>(b =>
        {
            b.ToTable("inbox_messages");
            b.HasKey(m => new { m.MessageId, m.Consumer });
            b.Property(m => m.Consumer).HasMaxLength(100);
            b.Property(m => m.Type).HasMaxLength(100).IsRequired();
            b.HasIndex(m => m.ProcessedAt);
        });

        return modelBuilder;
    }
}

/// <summary>Écrit les événements dans l'outbox du DbContext courant (même transaction que le métier).</summary>
internal sealed class OutboxEventPublisher<TDbContext>(TDbContext db, TimeProvider clock) : IEventPublisher
    where TDbContext : DbContext
{
    public void Publish<T>(string topic, string key, T message)
        where T : notnull =>
        db.Set<OutboxMessage>().Add(OutboxMessage.From(topic, MessageEnvelope.Create(key, message, clock.GetUtcNow())));
}
