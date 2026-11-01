using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine@sha256:266d3c257594e195b309ae9d1805e54c788f1d7b375f56bf6c367aac6c917e7b").Build();

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
