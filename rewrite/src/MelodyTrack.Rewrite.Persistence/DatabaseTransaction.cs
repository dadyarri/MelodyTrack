using Npgsql;

namespace MelodyTrack.Rewrite.Persistence;

public sealed class DatabaseTransaction : IAsyncDisposable
{
    private readonly Database database;
    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;
    private bool completed;
    private bool disposed;

    internal DatabaseTransaction(Database database, NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        this.database = database;
        this.connection = connection;
        this.transaction = transaction;
    }

    public DatabaseQuery Query(string sql)
    {
        EnsureActive();
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        return new DatabaseQuery(database, sql, this);
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.CommitAsync(cancellationToken);
        completed = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        await transaction.RollbackAsync(cancellationToken);
        completed = true;
    }

    internal Task<T> RunAsync<T>(Func<NpgsqlConnection, NpgsqlTransaction, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        EnsureActive();
        cancellationToken.ThrowIfCancellationRequested();
        return operation(connection, transaction, cancellationToken);
    }

    private void EnsureActive()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (completed)
        {
            throw new InvalidOperationException("Транзакция уже завершена.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        try
        {
            await transaction.DisposeAsync();
        }
        finally
        {
            await connection.DisposeAsync();
        }
    }
}
