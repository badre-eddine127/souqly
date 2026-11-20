using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Souqly.Messaging;

/// <summary>
/// Traces OpenTelemetry de la messagerie (conventions « messaging » d'OpenTelemetry) : le traitement d'un
/// message est rattaché à la requête HTTP d'origine grâce à l'en-tête traceparent, donc une commande se suit
/// de bout en bout dans le tableau de bord, à travers Kafka (US-19).
/// </summary>
public static class MessagingTelemetry
{
    public const string SourceName = "Souqly.Messaging";

    private static readonly ActivitySource Source = new(SourceName);

    private static readonly Meter Meter = new(SourceName, "1.0");

    // souqly_messaging_processed_total{group, type, outcome} et souqly_messaging_dead_letters_total{group, type}
    private static readonly Counter<long> Processed = Meter.CreateCounter<long>("souqly.messaging.processed", "{message}", "Messages traités par consommateur");

    private static readonly Counter<long> DeadLetters = Meter.CreateCounter<long>("souqly.messaging.dead_letters", "{message}", "Messages envoyés en DLQ après tous les essais");

    private static readonly Counter<long> PublishFailures = Meter.CreateCounter<long>("souqly.messaging.publish_failures", "{message}", "Envois au broker en échec (le message reste dans l'outbox)");

    internal static void RecordProcessed(string group, string type, DispatchOutcome outcome) =>
        Processed.Add(1, new("group", group), new("type", type), new("outcome", outcome.ToString().ToLowerInvariant()));

    internal static void RecordDeadLetter(string group, string type) => DeadLetters.Add(1, new("group", group), new("type", type));

    internal static void RecordPublishFailure(string topic) => PublishFailures.Add(1, new KeyValuePair<string, object?>("topic", topic));

    internal static Activity? StartPublish(string topic, MessageEnvelope message)
    {
        ActivityContext.TryParse(message.TraceParent, null, out var parent);
        var activity = Source.StartActivity($"send {topic}", ActivityKind.Producer, parent);
        return Tag(activity, topic, message, "send");
    }

    internal static Activity? StartConsume(string topic, MessageEnvelope message, string group)
    {
        ActivityContext.TryParse(message.TraceParent, null, out var parent);
        var activity = Source.StartActivity($"process {message.Type}", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.consumer.group.name", group);
        return Tag(activity, topic, message, "process");
    }

    private static Activity? Tag(Activity? activity, string topic, MessageEnvelope message, string operation)
    {
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.operation.type", operation);
        activity?.SetTag("messaging.destination.name", topic);
        activity?.SetTag("messaging.message.id", message.Id.ToString());
        activity?.SetTag("messaging.kafka.message.key", message.Key);
        activity?.SetTag("souqly.message.type", message.Type);
        return activity;
    }
}
