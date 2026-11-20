using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Souqly.Inventory.Domain;
using Souqly.Inventory.Infrastructure.Persistence;

namespace Souqly.Inventory.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("InventoryDb")
            ?? throw new InvalidOperationException("ConnectionStrings:InventoryDb manquante");

        services.AddDbContext<InventoryDbContext>(o => o
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IStockStore, StockStore>();

        if (configuration.GetValue("Inventory:ExpiryWorkerEnabled", true))
        {
            services.AddHostedService<ReservationExpiryWorker>();
        }

        return services;
    }
}
