using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Souqly.Catalog.Application.Vendors;
using Souqly.Catalog.Domain.Products;
using Souqly.Catalog.Domain.Vendors;
using Souqly.Catalog.Infrastructure.Identity;
using Souqly.Catalog.Infrastructure.Persistence;

namespace Souqly.Catalog.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCatalogInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("CatalogDb")
            ?? throw new InvalidOperationException("ConnectionStrings:CatalogDb manquante");

        services.AddDbContext<CatalogDbContext>(o => o
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<IVendorApplicationRepository, VendorApplicationRepository>();

        // US-16 : attribution du rôle VENDOR par l'API d'administration Keycloak (compte technique catalog-service).
        services.Configure<KeycloakAdminOptions>(configuration.GetSection(KeycloakAdminOptions.Section));
        var keycloak = configuration[$"{KeycloakAdminOptions.Section}:BaseAddress"] ?? "http://localhost:8180";
        services.AddHttpClient<IVendorRoleGranter, KeycloakVendorRoleGranter>(c => c.BaseAddress = new Uri(keycloak.TrimEnd('/') + "/"))
            .AddStandardResilienceHandler(o =>
            {
                o.Retry.MaxRetryAttempts = 2;
                o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(3);
                o.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(10);
            });
        return services;
    }
}
