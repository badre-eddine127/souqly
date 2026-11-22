using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Souqly.Catalog.Application.Vendors;
using Souqly.Testing;

namespace Catalog.ApiTests;

/// <summary>Démarre le vrai service Catalog en mémoire, branché sur une vraie base PostgreSQL jetable.</summary>
public sealed class CatalogApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgresFixture _postgres = new();

    /// <summary>Rôles VENDOR attribués pendant les tests (à la place de l'API d'administration Keycloak).</summary>
    public RecordingRoleGranter Roles { get; } = new();

    public async Task InitializeAsync() => await _postgres.InitializeAsync();

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureTestServices(services =>
        {
            services.UseTestJwt();
            services.AddSingleton<IVendorRoleGranter>(Roles);   // pas de Keycloak : on enregistre les attributions
        });
        builder.UseSetting("ConnectionStrings:CatalogDb", _postgres.ConnectionString);
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Messaging:Transport", "InMemory");   // les événements produit restent en mémoire
    }

    /// <summary>Client HTTP authentifié par un JWT de test (format Keycloak) avec l'identité et les rôles donnés.</summary>
    public HttpClient ClientAs(string userId, params string[] roles)
    {
        return CreateClient().WithToken(TestJwt.Token(userId, roles));
    }
}

public sealed class RecordingRoleGranter : IVendorRoleGranter
{
    public ConcurrentBag<string> Granted { get; } = [];

    public Task GrantVendorRoleAsync(string userId, CancellationToken ct)
    {
        Granted.Add(userId);
        return Task.CompletedTask;
    }
}
