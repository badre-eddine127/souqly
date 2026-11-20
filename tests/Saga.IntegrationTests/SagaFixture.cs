extern alias inventory;
extern alias notification;
extern alias ordering;
extern alias payment;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Souqly.Messaging;
using Souqly.Testing;

namespace Saga.IntegrationTests;

/// <summary>
/// Plateforme complète pour la saga : Ordering, Inventory, Payment et Notification avec chacun sa base PostgreSQL
/// (comme en production), reliés par un broker partagé. Par défaut le broker est en mémoire ; dans la CI,
/// les mêmes services tournent aussi sur un vrai Kafka (voir KafkaSagaTests).
/// </summary>
public class SagaFixture : IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();
    private readonly List<IAsyncDisposable> _hosts = [];

    // Écoute toutes les sources de traces : comme avec OpenTelemetry branché, ASP.NET Core et la messagerie créent leurs spans.
    private static readonly System.Diagnostics.ActivityListener Tracing = new()
    {
        ShouldListenTo = _ => true,
        Sample = (ref System.Diagnostics.ActivityCreationOptions<System.Diagnostics.ActivityContext> _) => System.Diagnostics.ActivitySamplingResult.AllData,
    };

    public InMemoryBroker Broker { get; } = new();

    public FakeCatalogServer Catalog { get; } = new();

    public HttpClient Ordering { get; private set; } = null!;
    public HttpClient Inventory { get; private set; } = null!;
    public HttpClient Payment { get; private set; } = null!;
    public HttpClient Notification { get; private set; } = null!;

    internal ServiceHost<ordering::Program> OrderingHost { get; private set; } = null!;
    internal ServiceHost<inventory::Program> InventoryHost { get; private set; } = null!;
    internal ServiceHost<payment::Program> PaymentHost { get; private set; } = null!;
    internal ServiceHost<notification::Program> NotificationHost { get; private set; } = null!;

    /// <summary>Adresse d'un vrai Kafka ; null = broker en mémoire.</summary>
    protected virtual string? KafkaBootstrapServers => null;

    public virtual async Task InitializeAsync()
    {
        System.Diagnostics.ActivitySource.AddActivityListener(Tracing);
        await _postgres.InitializeAsync();
        await Catalog.InitializeAsync();

        OrderingHost = Track(new ServiceHost<ordering::Program>(this, "OrderDb", await _postgres.CreateDatabaseAsync("ordering"), s =>
        {
            s["Catalog:BaseAddress"] = Catalog.Address;
            s["ServiceAccount:TokenEndpoint"] = Catalog.Address + "/token";
            s["ServiceAccount:ClientSecret"] = "test-secret";
        }));
        InventoryHost = Track(new ServiceHost<inventory::Program>(this, "InventoryDb", await _postgres.CreateDatabaseAsync("inventory"),
            s => s["Inventory:ExpiryWorkerEnabled"] = "false"));
        PaymentHost = Track(new ServiceHost<payment::Program>(this, "PaymentDb", await _postgres.CreateDatabaseAsync("payment"),
            s => s["Payment:DeclineAbove"] = "10000"));
        NotificationHost = Track(new ServiceHost<notification::Program>(this, "NotificationDb", await _postgres.CreateDatabaseAsync("notification"), _ => { }));

        // CreateClient démarre l'hôte (migrations, consommateurs, publieur d'outbox).
        Ordering = OrderingHost.CreateClient();
        Inventory = InventoryHost.CreateClient();
        Payment = PaymentHost.CreateClient();
        Notification = NotificationHost.CreateClient();
    }

    public virtual async Task DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }

        await Catalog.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private ServiceHost<T> Track<T>(ServiceHost<T> host)
        where T : class
    {
        _hosts.Add(host);
        return host;
    }

    internal sealed class ServiceHost<TProgram>(SagaFixture fixture, string connectionName, string connectionString, Action<Dictionary<string, string>> settings)
        : WebApplicationFactory<TProgram>
        where TProgram : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            var values = new Dictionary<string, string>
            {
                [$"ConnectionStrings:{connectionName}"] = connectionString,
                ["Database:MigrateOnStartup"] = "true",
                ["Messaging:OutboxPollInterval"] = "00:00:00.100",
                ["Messaging:RetryBaseDelay"] = "00:00:00.050",
            };
            if (fixture.KafkaBootstrapServers is { } kafka)
            {
                values["Messaging:Transport"] = "Kafka";
                values["ConnectionStrings:Kafka"] = kafka;
            }
            else
            {
                values["Messaging:Transport"] = "InMemory";
            }

            settings(values);
            builder.UseEnvironment("Testing");
            foreach (var (key, value) in values)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.UseTestJwt();
                services.RemoveAll<InMemoryBroker>();
                services.AddSingleton(fixture.Broker);   // un seul broker pour les quatre services
            });
        }
    }
}
