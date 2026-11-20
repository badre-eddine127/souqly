using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Souqly.Testing;

namespace Inventory.ApiTests;

public sealed class InventoryApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    public string ConnectionString => _postgres.ConnectionString;

    public async Task InitializeAsync() => await _postgres.InitializeAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:InventoryDb", _postgres.ConnectionString + ";Maximum Pool Size=60");
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Inventory:ExpiryWorkerEnabled", "false");   // les tests déclenchent l'expiration eux-mêmes
    }

    public HttpClient ClientAs(string userId, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", userId);
        client.DefaultRequestHeaders.Add("X-User-Roles", string.Join(',', roles));
        return client;
    }
}
