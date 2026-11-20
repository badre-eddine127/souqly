using Souqly.Testing;

namespace Saga.IntegrationTests;

/// <summary>Les quatre services reliés par un vrai broker Kafka (conteneur Testcontainers).</summary>
public sealed class KafkaSagaFixture : SagaFixture
{
    private readonly KafkaFixture _kafka = new();

    protected override string? KafkaBootstrapServers => _kafka.BootstrapServers;

    public override async Task InitializeAsync()
    {
        await _kafka.InitializeAsync();
        await base.InitializeAsync();
    }

    public override async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _kafka.DisposeAsync();
    }
}

/// <summary>
/// Mêmes scénarios que InMemorySagaTests, mais à travers Kafka : sérialisation, en-têtes, groupes de
/// consommateurs, commit des offsets. Exécutés dans la CI (Docker) ; en local : SOUQLY_TEST_KAFKA=localhost:9092.
/// </summary>
[Trait("Category", "Kafka")]
public sealed class KafkaSagaTests(KafkaSagaFixture platform) : SagaScenarios(platform), IClassFixture<KafkaSagaFixture>;
