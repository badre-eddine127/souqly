using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Souqly.Messaging;

/// <summary>Association topic + type de message → gestionnaire.</summary>
internal sealed record HandlerRegistration(string Topic, string Type, Type MessageType, Func<IServiceProvider, object, MessageContext, CancellationToken, Task> Invoke);

/// <summary>Liste des gestionnaires déclarés par le service.</summary>
public sealed class HandlerRegistry
{
    private readonly List<HandlerRegistration> _registrations = [];

    /// <summary>Topics auxquels le service s'abonne.</summary>
    public IReadOnlyList<string> Topics => _registrations.Select(r => r.Topic).Distinct().ToList();

    internal void Add(HandlerRegistration registration) => _registrations.Add(registration);

    internal IReadOnlyList<HandlerRegistration> For(string topic, string type) =>
        _registrations.Where(r => r.Topic == topic && r.Type == type).ToList();
}

public enum DispatchOutcome
{
    Handled,
    Duplicate,
    Ignored,
}

/// <summary>
/// Exécute les gestionnaires d'un message dans UNE transaction avec l'inbox et l'outbox :
/// soit tout est enregistré (effet métier + « déjà traité » + événements suivants), soit rien.
/// Un message relivré (Kafka garantit « au moins une fois ») est reconnu par l'inbox et ignoré.
/// </summary>
public interface IMessageDispatcher
{
    Task<DispatchOutcome> DispatchAsync(string topic, MessageEnvelope message, CancellationToken ct);
}

internal sealed partial class MessageDispatcher<TDbContext>(
    IServiceScopeFactory scopes,
    HandlerRegistry registry,
    MessagingOptions options,
    ILogger<MessageDispatcher<TDbContext>> logger) : IMessageDispatcher
    where TDbContext : DbContext
{
    public async Task<DispatchOutcome> DispatchAsync(string topic, MessageEnvelope message, CancellationToken ct)
    {
        var handlers = registry.For(topic, message.Type);
        if (handlers.Count == 0)
        {
            return DispatchOutcome.Ignored;   // événement qui ne concerne pas ce service
        }

        using var activity = MessagingTelemetry.StartConsume(topic, message, options.ConsumerGroup);
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var inbox = db.Set<InboxMessage>();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        if (await inbox.AnyAsync(i => i.MessageId == message.Id && i.Consumer == options.ConsumerGroup, ct))
        {
            LogDuplicate(logger, message.Type, message.Id);
            activity?.SetTag("souqly.messaging.duplicate", true);
            return DispatchOutcome.Duplicate;
        }

        var context = new MessageContext(message.Id, topic, message.Type, message.Key, message.OccurredAt);
        foreach (var handler in handlers)
        {
            var payload = JsonSerializer.Deserialize(message.Payload, handler.MessageType, MessageEnvelope.Json)
                          ?? throw new JsonException($"Message {message.Type} vide.");
            await handler.Invoke(scope.ServiceProvider, payload, context, ct);
        }

        inbox.Add(new InboxMessage
        {
            MessageId = message.Id,
            Consumer = options.ConsumerGroup,
            Type = message.Type,
            ProcessedAt = DateTimeOffset.UtcNow,
        });

        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return DispatchOutcome.Handled;
        }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, TableName: "inbox_messages" })
        {
            // Le même message a été traité en parallèle par une autre instance : elle a gagné, on annule tout.
            await transaction.RollbackAsync(ct);
            LogDuplicate(logger, message.Type, message.Id);
            return DispatchOutcome.Duplicate;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Message {Type} {MessageId} déjà traité : ignoré")]
    private static partial void LogDuplicate(ILogger logger, string type, Guid messageId);
}

/// <summary>
/// Boucle commune aux transports : quelques essais avec attente croissante, puis envoi dans le topic DLQ
/// pour ne pas bloquer la partition (un message « empoisonné » ne doit pas arrêter toutes les commandes).
/// </summary>
internal static partial class ResilientDispatch
{
    public static async Task RunAsync(
        IMessageDispatcher dispatcher, IMessageTransport transport, MessagingOptions options,
        string topic, MessageEnvelope message, ILogger logger, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await dispatcher.DispatchAsync(topic, message, ct);
                return;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                if (attempt >= options.MaxAttempts)
                {
                    LogDeadLetter(logger, e, message.Type, message.Id, attempt);
                    Activity.Current?.AddException(e);
                    await transport.PublishAsync(Topics.DeadLetter(topic), message with { TraceParent = Activity.Current?.Id ?? message.TraceParent }, ct);
                    return;
                }

                LogRetry(logger, e, message.Type, message.Id, attempt);
                await Task.Delay(options.RetryBaseDelay * Math.Pow(2, attempt - 1), ct);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Échec du traitement de {Type} {MessageId} (essai {Attempt}) : nouvel essai")]
    private static partial void LogRetry(ILogger logger, Exception e, string type, Guid messageId, int attempt);

    [LoggerMessage(Level = LogLevel.Error, Message = "Message {Type} {MessageId} envoyé en DLQ après {Attempt} essais")]
    private static partial void LogDeadLetter(ILogger logger, Exception e, string type, Guid messageId, int attempt);
}
