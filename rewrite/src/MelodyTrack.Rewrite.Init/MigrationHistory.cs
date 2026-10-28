using Npgsql;

namespace MelodyTrack.Rewrite.Init;

public static class MigrationHistory
{
    private sealed record AppliedMigration(long Version, string Name, string Checksum);

    public static async Task<Migration[]> GetPendingAsync(NpgsqlConnection connection, IReadOnlyList<Migration> migrations, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT version, name, checksum FROM public.schema_migrations ORDER BY version", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var applied = new List<AppliedMigration>();
        while (await reader.ReadAsync(cancellationToken)) applied.Add(new(reader.GetInt64(0), reader.GetString(1), reader.GetString(2)));

        var byVersion = migrations.ToDictionary(migration => migration.Version);
        foreach (var previous in applied)
        {
            if (!byVersion.TryGetValue(previous.Version, out var current)) throw new InvalidMigrationException($"Не найден файл применённой миграции {previous.Name}.");
            if (current.Name != previous.Name) throw new InvalidMigrationException($"Применённая миграция {previous.Name} переименована в {current.Name}.");
            if (current.Checksum != previous.Checksum) throw new InvalidMigrationException($"Изменено содержимое применённой миграции {previous.Name}.");
        }
        var appliedVersions = applied.Select(migration => migration.Version).ToHashSet();
        var pending = migrations.Where(migration => !appliedVersions.Contains(migration.Version)).ToArray();
        var latestApplied = applied.Count == 0 ? 0 : applied[^1].Version;
        var outOfOrder = pending.FirstOrDefault(migration => migration.Version < latestApplied);
        if (outOfOrder is not null) throw new InvalidMigrationException($"Новая миграция {outOfOrder.Name} имеет номер ниже уже применённого {latestApplied}.");
        return pending;
    }
}
