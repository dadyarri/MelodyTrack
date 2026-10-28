using Dapper;
using Npgsql;

namespace MelodyTrack.Rewrite.Persistence;

public sealed class Database
{
    private readonly NpgsqlDataSource dataSource;

    public Database(NpgsqlDataSource dataSource)
    {
        this.dataSource = dataSource;
    }

    public DatabaseQuery Query(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return new DatabaseQuery(this, sql);
    }

    public async Task<DatabaseTransaction> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            return new DatabaseTransaction(this, connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    internal async Task<T> WithConnectionAsync<T>(Func<NpgsqlConnection, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await operation(connection, cancellationToken);
    }

    public static async Task CheckConnectionAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await connection.ExecuteScalarAsync<int>(new CommandDefinition("SELECT 1", cancellationToken: cancellationToken));
    }
}
