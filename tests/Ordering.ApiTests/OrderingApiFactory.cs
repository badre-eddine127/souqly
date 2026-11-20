using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Souqly.Testing;

namespace Ordering.ApiTests;

public sealed class OrderingApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public FakeCatalogServer Catalog { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();
        await Catalog.InitializeAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Catalog.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services => services.UseTestJwt());
        builder.UseSetting("ConnectionStrings:OrderDb", _postgres.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Messaging:Transport", "InMemory");   // pas de Kafka dans ces tests : la saga est testée dans Saga.IntegrationTests
        builder.UseSetting("Catalog:BaseAddress", Catalog.Address);
        builder.UseSetting("ServiceAccount:TokenEndpoint", Catalog.Address + "/token");
        builder.UseSetting("ServiceAccount:ClientSecret", "test-secret");
    }

    public HttpClient ClientAs(string userId, params string[] roles) => CreateClient().WithToken(TestJwt.Token(userId, roles));
}
