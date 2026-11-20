using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Souqly.Messaging;

/// <summary>
/// Envoie au broker les messages de l'outbox, dans l'ordre d'écriture.
/// <c>FOR UPDATE SKIP LOCKED</c> : plusieurs instances du service peuvent tourner sans envoyer deux fois le même lot.
/// Si le broker est indisponible, les messages restent en base et partent dès son retour (aucune perte).
/// </summary>
internal sealed partial class OutboxPublisher<TDbContext>(
    IServiceScopeFactory scopes,
    IMessageTransport transport,
    MessagingOptions options,
    TimeProvider clock,
    ILogger<OutboxPublisher<TDbContext>> logger) : BackgroundService
    where TDbContext : DbContext
{
    private const int BatchSize = 100;
    private DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var sent = 0;
            try
            {
                sent = await PublishBatchAsync(stoppingToken);
                await CleanupAsync(stoppingToken);
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                LogBatchFailed(logger, e);
            }

            if (sent < BatchSize)
            {
                await Task.Delay(options.OutboxPollInterval, stoppingToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    internal async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var batch = await db.Set<OutboxMessage>()
            .FromSql($"SELECT * FROM outbox_messages WHERE published_at IS NULL ORDER BY occurred_at LIMIT {BatchSize} FOR UPDATE SKIP LOCKED")
            .ToListAsync(ct);

        var sent = 0;
        foreach (var message in batch)
        {
            try
            {
                var envelope = message.ToEnvelope();
                using (var activity = MessagingTelemetry.StartPublish(message.Topic, envelope))
                {
                    // Le consommateur se rattache à ce span « send », lui-même enfant de la requête d'origine.
                    await transport.PublishAsync(message.Topic, envelope with { TraceParent = activity?.Id ?? envelope.TraceParent }, ct);
                }

                message.PublishedAt = clock.GetUtcNow();
                sent++;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                message.Attempts++;
                message.LastError = e.Message.Length > 1000 ? e.Message[..1000] : e.Message;
                LogPublishFailed(logger, e, message.Type, message.Id);
                break;   // on s'arrête pour garder l'ordre des événements d'une même commande
            }
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return sent;
    }

    /// <summary>Supprime les messages envoyés et les traces d'inbox de plus de 7 jours (une fois par heure).</summary>
    private async Task CleanupAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        if (now < _nextCleanup)
        {
            return;
        }

        _nextCleanup = now.AddHours(1);
        var limit = now - options.Retention;
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();
        await db.Set<OutboxMessage>().Where(m => m.PublishedAt != null && m.PublishedAt < limit).ExecuteDeleteAsync(ct);
        await db.Set<InboxMessage>().Where(m => m.ProcessedAt < limit).ExecuteDeleteAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Envoi du message {Type} {MessageId} impossible : il reste dans l'outbox")]
    private static partial void LogPublishFailed(ILogger logger, Exception e, string type, Guid messageId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lecture de l'outbox impossible")]
    private static partial void LogBatchFailed(ILogger logger, Exception e);
}
