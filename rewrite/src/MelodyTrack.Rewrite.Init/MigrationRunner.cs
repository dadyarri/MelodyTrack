using Microsoft.Extensions.Logging;
using Npgsql;

namespace MelodyTrack.Rewrite.Init;

public static class MigrationRunner
{
    // Stable session lock key: ASCII "MTRACKIN". All Init versions use the same key.
    private const long LockKey = 0x4D545241434B494E;

    public static async Task PrepareAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var lockCommand = new NpgsqlCommand("SELECT pg_try_advisory_lock($1)", connection);
        lockCommand.Parameters.AddWithValue(LockKey);
        if (await lockCommand.ExecuteScalarAsync(cancellationToken) is not true) throw new MigrationLockUnavailableException();
        await using var historyCommand = new NpgsqlCommand("""
            CREATE TABLE IF NOT EXISTS public.schema_migrations (
                version bigint PRIMARY KEY CHECK (version > 0),
                name text NOT NULL,
                checksum text NOT NULL CHECK (checksum ~ '^[0-9A-F]{64}$'),
                applied_at timestamptz NOT NULL DEFAULT clock_timestamp()
            );
            """, connection);
        await historyCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task ApplyAsync(NpgsqlConnection connection, Migration migration, ILogger logger, CancellationToken cancellationToken)
    {
        try
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await migration.RunAsync(new(connection, transaction, cancellationToken, logger));
            await using var history = new NpgsqlCommand("INSERT INTO public.schema_migrations (version, name, checksum) VALUES ($1, $2, $3)", connection, transaction);
            history.Parameters.AddWithValue(migration.Version);
            history.Parameters.AddWithValue(migration.Name);
            history.Parameters.AddWithValue(migration.Checksum);
            await history.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception error)
        {
            throw new MigrationFailedException(migration.Name, error);
        }
    }
}
