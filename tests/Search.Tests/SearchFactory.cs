using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Souqly.Messaging;
using Souqly.Search.Api.Application;
using Souqly.Testing;

namespace Search.Tests;

public sealed class SearchFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public async Task InitializeAsync() => await _postgres.InitializeAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services => services.UseTestJwt());
        builder.UseSetting("ConnectionStrings:SearchDb", _postgres.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Messaging:Transport", "InMemory");
        builder.UseSetting("Search:BackfillFromCatalog", "false");   // pas de Catalog ici : les tests envoient les événements
    }

    /// <summary>Remet un événement de Catalog au service, comme le ferait le consommateur Kafka (inbox comprise).</summary>
    public async Task<DispatchOutcome> DeliverAsync<T>(long productId, T message, Guid? messageId = null)
        where T : notnull
    {
        _ = Server;   // démarre l'hôte (migrations)
        var envelope = MessageEnvelope.Create(productId.ToString(System.Globalization.CultureInfo.InvariantCulture), message, DateTimeOffset.UtcNow);
        if (messageId is { } id)
        {
            envelope = envelope with { Id = id };
        }

        return await Services.GetRequiredService<IMessageDispatcher>().DispatchAsync(Topics.CatalogEvents, envelope, CancellationToken.None);
    }

    public static ProductSnapshot Product(long id, string name, decimal price, long category = 2, string status = "ACTIVE",
        string description = "", DateTimeOffset? updatedAt = null) =>
        new(id, "fatima", category, $"SKU-{id}", name, description, price, "MAD", status, updatedAt ?? DateTimeOffset.UtcNow);
}
