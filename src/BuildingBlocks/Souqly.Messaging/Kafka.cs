using System.Globalization;
using System.Text;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Souqly.Messaging;

/// <summary>Producteur Kafka : acks=all et idempotence activée (pas de doublon dû aux réessais réseau du producteur).</summary>
internal sealed class KafkaTransport : IMessageTransport, IDisposable
{
    private readonly IProducer<string, string> _producer;

    public KafkaTransport(MessagingOptions options)
    {
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = options.BootstrapServers,
            ClientId = options.ConsumerGroup,
            Acks = Acks.All,
            EnableIdempotence = true,
            MessageTimeoutMs = 10_000,
        }).Build();
    }

    public async Task PublishAsync(string topic, MessageEnvelope message, CancellationToken ct)
    {
        var headers = new Headers
        {
            { KafkaHeaders.MessageId, Encoding.UTF8.GetBytes(message.Id.ToString()) },
            { KafkaHeaders.MessageType, Encoding.UTF8.GetBytes(message.Type) },
            { KafkaHeaders.OccurredAt, Encoding.UTF8.GetBytes(message.OccurredAt.ToString("O", CultureInfo.InvariantCulture)) },
        };
        if (message.TraceParent is not null)
        {
            headers.Add(KafkaHeaders.TraceParent, Encoding.UTF8.GetBytes(message.TraceParent));
        }

        await _producer.ProduceAsync(topic, new Message<string, string> { Key = message.Key, Value = message.Payload, Headers = headers }, ct);
    }

    public void Dispose()
    {
        _producer.Flush(TimeSpan.FromSeconds(5));
        _producer.Dispose();
    }
}

internal static class KafkaHeaders
{
    public const string MessageId = "message-id";
    public const string MessageType = "message-type";
    public const string OccurredAt = "occurred-at";
    public const string TraceParent = "traceparent";

    public static MessageEnvelope ToEnvelope(ConsumeResult<string, string> result)
    {
        string? Get(string name) =>
            result.Message.Headers.TryGetLastBytes(name, out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

        return new MessageEnvelope(
            Guid.TryParse(Get(MessageId), out var id) ? id : Guid.Empty,
            Get(MessageType) ?? "unknown",
            result.Message.Key ?? string.Empty,
            DateTimeOffset.TryParse(Get(OccurredAt), CultureInfo.InvariantCulture, DateTimeStyles.None, out var at) ? at : result.Message.Timestamp.UtcDateTime,
            result.Message.Value ?? string.Empty,
            Get(TraceParent));
    }
}

/// <summary>
/// Consommateur Kafka du service (un groupe par service). Validation manuelle de l'offset APRÈS traitement :
/// en cas de panne, le message est relu (« au moins une fois ») et l'inbox évite un double effet.
/// </summary>
internal sealed partial class KafkaConsumerHost(
    IMessageDispatcher dispatcher,
    IMessageTransport transport,
    HandlerRegistry registry,
    MessagingOptions options,
    ILogger<KafkaConsumerHost> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        // Consume() est bloquant : boucle sur un thread dédié pour ne pas bloquer le démarrage de l'hôte.
        Task.Factory.StartNew(() => RunAsync(stoppingToken), stoppingToken, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task RunAsync(CancellationToken ct)
    {
        if (registry.Topics.Count == 0)
        {
            return;
        }

        await KafkaTopics.EnsureCreatedAsync(options, registry.Topics, logger);

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = options.BootstrapServers,
            GroupId = options.ConsumerGroup,
            ClientId = options.ConsumerGroup,
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky,
        }).Build();
        consumer.Subscribe(registry.Topics);
        LogSubscribed(logger, options.ConsumerGroup, string.Join(", ", registry.Topics));

        try
        {
            while (!ct.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result;
                try
                {
                    result = consumer.Consume(ct);
                }
                catch (ConsumeException e) when (!e.Error.IsFatal)
                {
                    LogConsumeError(logger, e.Error.Reason);
                    continue;
                }

                if (result?.Message is null)
                {
                    continue;
                }

                await ResilientDispatch.RunAsync(dispatcher, transport, options, result.Topic, KafkaHeaders.ToEnvelope(result), logger, ct);
                consumer.Commit(result);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // arrêt normal
        }
        finally
        {
            consumer.Close();   // quitte le groupe proprement : rééquilibrage immédiat
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Groupe {Group} abonné à {Topics}")]
    private static partial void LogSubscribed(ILogger logger, string group, string topics);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Erreur de lecture Kafka : {Reason}")]
    private static partial void LogConsumeError(ILogger logger, string reason);
}

internal static partial class KafkaTopics
{
    /// <summary>
    /// Crée les topics Souqly, ceux auxquels le service s'abonne, et leurs DLQ, s'ils n'existent pas
    /// (3 partitions ; sans effet s'ils existent). Un abonnement à un topic absent ne verrait ses messages
    /// qu'au prochain rafraîchissement des métadonnées (plusieurs minutes).
    /// </summary>
    public static async Task EnsureCreatedAsync(MessagingOptions options, IEnumerable<string> subscribed, ILogger logger)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = options.BootstrapServers }).Build();
        var topics = Topics.All.Concat(subscribed).Distinct().ToList();
        var specs = topics.Concat(topics.Select(Topics.DeadLetter))
            .Select(t => new TopicSpecification { Name = t, NumPartitions = options.Partitions, ReplicationFactor = -1 })
            .ToList();
        try
        {
            await admin.CreateTopicsAsync(specs);
        }
        catch (CreateTopicsException e) when (e.Results.All(r => r.Error.Code is ErrorCode.NoError or ErrorCode.TopicAlreadyExists))
        {
            // déjà créés par un autre service
        }
        catch (CreateTopicsException e)
        {
            LogCreateFailed(logger, string.Join("; ", e.Results.Where(r => r.Error.IsError).Select(r => $"{r.Topic}: {r.Error.Reason}")));
        }
        catch (KafkaException e)
        {
            LogCreateFailed(logger, e.Message);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Création des topics impossible : {Reason} (création automatique du broker utilisée)")]
    private static partial void LogCreateFailed(ILogger logger, string reason);
}
