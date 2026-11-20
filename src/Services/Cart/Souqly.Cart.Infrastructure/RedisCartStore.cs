using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Souqly.Cart.Domain;
using StackExchange.Redis;

namespace Souqly.Cart.Infrastructure;

/// <summary>
/// Panier stocké dans Redis sous la clé "souqly:cart:{customerId}", en JSON, avec une durée de vie de 30 jours
/// repoussée à chaque modification. Le service reste sans état : on peut lancer N répliques.
/// </summary>
internal sealed class RedisCartStore(IConnectionMultiplexer redis) : ICartStore
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static RedisKey Key(string customerId) => $"souqly:cart:{customerId}";

    public async Task<ShoppingCart?> FindAsync(string customerId, CancellationToken ct)
    {
        var value = await redis.GetDatabase().StringGetAsync(Key(customerId));
        if (value.IsNullOrEmpty)
        {
            return null;
        }

        var stored = JsonSerializer.Deserialize<StoredCart>(value.ToString(), Json)!;
        return new ShoppingCart(customerId, stored.Lines, stored.UpdatedAt);
    }

    public Task SaveAsync(ShoppingCart cart, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new StoredCart(cart.Lines.ToDictionary(), cart.UpdatedAt), Json);
        return redis.GetDatabase().StringSetAsync(Key(cart.CustomerId), payload, ShoppingCart.Lifetime);
    }

    public Task DeleteAsync(string customerId, CancellationToken ct) =>
        redis.GetDatabase().KeyDeleteAsync(Key(customerId));

    private sealed record StoredCart(Dictionary<long, int> Lines, DateTimeOffset UpdatedAt);
}

public static class DependencyInjection
{
    public static IServiceCollection AddCartInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException("ConnectionStrings:Redis manquante");
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));
        services.AddSingleton<ICartStore, RedisCartStore>();
        return services;
    }
}
