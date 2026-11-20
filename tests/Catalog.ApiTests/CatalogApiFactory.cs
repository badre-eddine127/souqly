using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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
        builder.ConfigureTestServices(services => services.UseTestJwt());
        builder.UseSetting("ConnectionStrings:CatalogDb", _postgres.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
    }

    /// <summary>Client HTTP authentifié par un JWT de test (format Keycloak) avec l'identité et les rôles donnés.</summary>
    public HttpClient ClientAs(string userId, params string[] roles)
    {
        return CreateClient().WithToken(TestJwt.Token(userId, roles));
    }
}
