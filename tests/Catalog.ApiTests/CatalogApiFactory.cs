using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Souqly.Testing;

namespace Catalog.ApiTests;

/// <summary>Démarre le vrai service Catalog en mémoire, branché sur une vraie base PostgreSQL jetable.</summary>
public sealed class CatalogApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
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
        builder.UseSetting("ConnectionStrings:CatalogDb", _postgres.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Authentication:Mode", "DevHeaders");
    }

    /// <summary>Client HTTP authentifié avec l'identité et les rôles donnés (en-têtes DevHeaders).</summary>
    public HttpClient ClientAs(string userId, params string[] roles)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-User-Id", userId);
        client.DefaultRequestHeaders.Add("X-User-Roles", string.Join(',', roles));
        return client;
    }
}
