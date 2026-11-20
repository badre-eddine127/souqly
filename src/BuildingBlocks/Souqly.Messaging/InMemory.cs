using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Souqly.Messaging;

/// <summary>
/// Broker en mémoire qui imite Kafka pour les tests : topics, groupes de consommateurs, ordre conservé par
/// groupe, livraison « au moins une fois ». Une seule instance peut être partagée par plusieurs services
/// démarrés dans le même processus de test, pour rejouer la saga complète sans Docker.
/// </summary>
public sealed class InMemoryBroker
{
    private readonly ConcurrentDictionary<string, Group> _groups = new();
    private readonly ConcurrentQueue<(string Topic, MessageEnvelope Message)> _log = new();
    private int _inFlight;

    /// <summary>Simule un broker qui relivre chaque message (redémarrage d'un consommateur avant le commit).</summary>
    public bool DeliverTwice { get; set; }

    /// <summary>Tous les messages publiés, dans l'ordre (pour les assertions des tests).</summary>
    public IReadOnlyList<(string Topic, MessageEnvelope Message)> Published => _log.ToArray();

    public void Publish(string topic, MessageEnvelope message)
    {
        _log.Enqueue((topic, message));
        foreach (var group in _groups.Values.Where(g => g.Topics.Contains(topic)))
        {
            var copies = DeliverTwice ? 2 : 1;
            for (var i = 0; i < copies; i++)
            {
                Interlocked.Increment(ref _inFlight);
                group.Queue.Writer.TryWrite((topic, message));
            }
        }
    }

    internal ChannelReader<(string Topic, MessageEnvelope Message)> Subscribe(string groupName, IEnumerable<string> topics)
    {
        var group = _groups.GetOrAdd(groupName, _ => new Group());
        foreach (var topic in topics)
        {
            group.Topics.Add(topic);
        }

        return group.Queue.Reader;
    }

    internal void Completed() => Interlocked.Decrement(ref _inFlight);

    /// <summary>Attend que tous les messages publiés (et ceux qu'ils déclenchent) soient traités.</summary>
    public async Task WaitUntilIdleAsync(TimeSpan timeout, Func<bool>? outboxEmpty = null)
    {
        var deadline = DateTime.UtcNow + timeout;
        var quietSince = DateTime.MaxValue;
        while (DateTime.UtcNow < deadline)
        {
            var idle = Volatile.Read(ref _inFlight) == 0 && (outboxEmpty?.Invoke() ?? true);
            if (!idle)
            {
                quietSince = DateTime.MaxValue;
            }
            else if (quietSince == DateTime.MaxValue)
            {
                quietSince = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - quietSince > TimeSpan.FromMilliseconds(600))
            {
                return;   // calme depuis plus d'un cycle de lecture d'outbox
            }

            await Task.Delay(50);
        }

        throw new TimeoutException("La saga ne s'est pas stabilisée à temps.");
    }

    private sealed class Group
    {
        public ConcurrentBag<string> Topics { get; } = [];

        public Channel<(string Topic, MessageEnvelope Message)> Queue { get; } =
            Channel.CreateUnbounded<(string, MessageEnvelope)>(new UnboundedChannelOptions { SingleReader = false });
    }
}

internal sealed class InMemoryTransport(InMemoryBroker broker) : IMessageTransport
{
    public Task PublishAsync(string topic, MessageEnvelope message, CancellationToken ct)
    {
        broker.Publish(topic, message);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryConsumerHost(
    InMemoryBroker broker,
    IMessageDispatcher dispatcher,
    IMessageTransport transport,
    HandlerRegistry registry,
    MessagingOptions options,
    ILogger<InMemoryConsumerHost> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (registry.Topics.Count == 0)
        {
            return;
        }

        var reader = broker.Subscribe(options.ConsumerGroup, registry.Topics);
        try
        {
            await foreach (var (topic, message) in reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ResilientDispatch.RunAsync(dispatcher, transport, options, topic, message, logger, stoppingToken);
                }
                finally
                {
                    broker.Completed();
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // arrêt normal
        }
    }
}
