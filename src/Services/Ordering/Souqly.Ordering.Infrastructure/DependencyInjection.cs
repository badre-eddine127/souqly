using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Polly;
using Souqly.Ordering.Application;
using Souqly.Ordering.Domain;
using Souqly.Ordering.Infrastructure.Catalog;
using Souqly.Ordering.Infrastructure.Persistence;

namespace Souqly.Ordering.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOrderingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("OrderDb")
            ?? throw new InvalidOperationException("ConnectionStrings:OrderDb manquante");
        services.AddDbContext<OrderingDbContext>(o => o
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history"))
            .UseSnakeCaseNamingConvention());
        services.AddScoped<IOrderRepository, OrderRepository>();

        services.Configure<ServiceAccountOptions>(configuration.GetSection(ServiceAccountOptions.Section));
        services.AddHttpClient(ClientCredentialsHandler.TokenClientName, c => c.Timeout = TimeSpan.FromSeconds(3));
        services.AddTransient<ClientCredentialsHandler>();

        var catalogAddress = configuration["Catalog:BaseAddress"] ?? "http://localhost:5101";
        services.AddHttpClient<ICatalogPort, CatalogHttpClient>(c => c.BaseAddress = new Uri(catalogAddress))
            .AddHttpMessageHandler<ClientCredentialsHandler>()
            .AddResilienceHandler("catalog", pipeline =>
            {
                // Budget total 2,8 s : la réponse 503 part en moins de 3 s si le catalogue est arrêté ou lent.
                pipeline.AddTimeout(TimeSpan.FromMilliseconds(2800));
                pipeline.AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 1,
                    Delay = TimeSpan.FromMilliseconds(200),
                    BackoffType = DelayBackoffType.Constant,
                });
                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 20,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(10),
                });
                pipeline.AddTimeout(TimeSpan.FromSeconds(1.2));   // délai par tentative
            });
        return services;
    }
}
