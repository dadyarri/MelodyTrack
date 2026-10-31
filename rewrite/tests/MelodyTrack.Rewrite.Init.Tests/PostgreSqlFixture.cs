using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:16-alpine@sha256:4e6e670bb069649261c9c18031f0aded7bb249a5b6664ddec29c013a89310d50").Build();

    public string ConnectionString(string database) => new NpgsqlConnectionStringBuilder(container.GetConnectionString())
    {
        Database = database,
        Pooling = false,
        Multiplexing = false
    }.ConnectionString;

    public async Task<string> CreateDatabaseAsync(CancellationToken cancellationToken)
    {
        var name = "migrations_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(container.GetConnectionString());
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return ConnectionString(name);
    }

    public ValueTask InitializeAsync() => new(container.StartAsync(TestContext.Current.CancellationToken));
    public ValueTask DisposeAsync() => container.DisposeAsync();
}
