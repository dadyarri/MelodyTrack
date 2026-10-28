using Microsoft.Extensions.Logging;
using Npgsql;

namespace MelodyTrack.Rewrite.Init;

public sealed record MigrationContext(NpgsqlConnection Connection, NpgsqlTransaction Transaction, CancellationToken CancellationToken, ILogger Logger);
public sealed record Migration(long Version, string Name, string Checksum, Func<MigrationContext, Task> RunAsync);
public sealed class InvalidMigrationException(string message) : Exception(message) { }
public sealed class MigrationLockUnavailableException : Exception { }
public sealed class MigrationFailedException(string migrationName, Exception innerException)
    : Exception($"Не удалось применить миграцию {migrationName}.", innerException)
{
    public string MigrationName { get; } = migrationName;
}

public static class SqlMigration
{
    public static async Task RunAsync(string sql, MigrationContext context)
    {
        await using var command = new NpgsqlCommand(sql, context.Connection, context.Transaction);
        await command.ExecuteNonQueryAsync(context.CancellationToken);
    }
}
