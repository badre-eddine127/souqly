using Testcontainers.Redis;
using Xunit;

namespace Souqly.Testing;

/// <summary>Redis pour les tests : Testcontainers par défaut, ou SOUQLY_TEST_REDIS (ex. "localhost:6379").</summary>
public sealed class RedisFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "SOUQLY_TEST_REDIS";

    private RedisContainer? _container;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(external))
        {
            ConnectionString = external;
            return;
        }

        _container = new RedisBuilder("redis:7-alpine").Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}
