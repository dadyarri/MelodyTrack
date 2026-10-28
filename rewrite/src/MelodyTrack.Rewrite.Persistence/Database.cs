using Npgsql;

namespace MelodyTrack.Rewrite.Persistence;

public static class Database
{
    public static async Task CheckConnectionAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }
}
