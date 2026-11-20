using Testcontainers.Kafka;
using Xunit;

namespace Souqly.Testing;

/// <summary>
/// Vrai broker Kafka pour les tests marqués Category=Kafka.
/// - Par défaut : conteneur Testcontainers (Docker requis, c'est le cas dans la CI GitHub).
/// - Sinon : SOUQLY_TEST_KAFKA pointe vers un broker existant (ex. "localhost:9092" lancé par Aspire).
/// </summary>
public sealed class KafkaFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "SOUQLY_TEST_KAFKA";

    private KafkaContainer? _container;

    public string BootstrapServers { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(external))
        {
            BootstrapServers = external;
            return;
        }

        _container = new KafkaBuilder("confluentinc/cp-kafka:7.5.12").Build();
        await _container.StartAsync();
        BootstrapServers = _container.GetBootstrapAddress();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
