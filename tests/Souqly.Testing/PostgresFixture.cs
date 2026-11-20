using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Souqly.Testing;

/// <summary>
/// Fournit une vraie base PostgreSQL 16 jetable pour les tests d'intégration (jamais InMemory ni SQLite :
/// on veut tester les vraies contraintes, la concurrence et les migrations).
/// - Par défaut : un conteneur Testcontainers (Docker requis).
/// - Sinon : la variable SOUQLY_TEST_POSTGRES pointe vers un serveur existant ; une base unique est créée puis supprimée.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public const string EnvironmentVariable = "SOUQLY_TEST_POSTGRES";

    private PostgreSqlContainer? _container;
    private string? _adminConnectionString;
    private string? _databaseName;

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        var external = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(external))
        {
            _adminConnectionString = external;
            _databaseName = "souqly_test_" + Guid.NewGuid().ToString("N")[..12];
            await ExecuteAsync(external, $"CREATE DATABASE {_databaseName}");
            ConnectionString = new NpgsqlConnectionStringBuilder(external) { Database = _databaseName }.ConnectionString;
            return;
        }

        _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _container.StartAsync();
        ConnectionString = _container.GetConnectionString();
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
            return;
        }

        if (_adminConnectionString is not null && _databaseName is not null)
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteAsync(_adminConnectionString, $"DROP DATABASE IF EXISTS {_databaseName} WITH (FORCE)");
        }
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
