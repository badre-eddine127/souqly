using System.Diagnostics;
using System.Text.Json;

namespace Souqly.Messaging;

/// <summary>
/// Message tel qu'il circule sur le bus. Sur Kafka : clé = <see cref="Key"/>, valeur = <see cref="Payload"/> (JSON),
/// le reste en en-têtes (message-id, message-type, occurred-at, traceparent).
/// </summary>
public sealed record MessageEnvelope(
    Guid Id,
    string Type,
    string Key,
    DateTimeOffset OccurredAt,
    string Payload,
    string? TraceParent)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static MessageEnvelope Create<T>(string key, T message, DateTimeOffset now)
        where T : notnull =>
        new(Guid.CreateVersion7(now), typeof(T).Name, key, now, JsonSerializer.Serialize(message, Json), Activity.Current?.Id);
}

/// <summary>Informations de transport transmises au gestionnaire d'un message.</summary>
public sealed record MessageContext(Guid MessageId, string Topic, string Type, string Key, DateTimeOffset OccurredAt);

/// <summary>Gestionnaire d'un type d'événement. Exécuté dans une transaction avec l'inbox et l'outbox du service.</summary>
public interface IMessageHandler<in TMessage>
{
    Task HandleAsync(TMessage message, MessageContext context, CancellationToken ct);
}

/// <summary>
/// Publie des événements de façon fiable : le message est écrit dans la table outbox par le même SaveChanges
/// que la modification métier, puis envoyé à Kafka par <c>OutboxPublisher</c>. Jamais de message perdu
/// ni de message pour une modification annulée.
/// </summary>
public interface IEventPublisher
{
    void Publish<T>(string topic, string key, T message)
        where T : notnull;
}

/// <summary>Transport vers le broker (Kafka en exécution, mémoire dans les tests).</summary>
public interface IMessageTransport
{
    Task PublishAsync(string topic, MessageEnvelope message, CancellationToken ct);
}
