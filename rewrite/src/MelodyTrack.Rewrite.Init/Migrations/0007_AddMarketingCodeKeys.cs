using MelodyTrack.Rewrite.Domain.Rules;
using Npgsql;

namespace MelodyTrack.Rewrite.Init.Migrations;

internal static class AddMarketingCodeKeys
{
    internal static async Task RunAsync(MigrationContext context)
    {
        var token = context.CancellationToken;
        token.ThrowIfCancellationRequested();
        // Keep the set stable until the enclosing migration transaction commits.
        await SqlMigration.RunAsync("LOCK TABLE public.marketing_campaigns IN ACCESS EXCLUSIVE MODE", context);
        var keys = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var rows = new List<(Guid Id, string Key)>();
        await using (var command = new NpgsqlCommand("SELECT id, code FROM public.marketing_campaigns", context.Connection, context.Transaction))
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            while (await reader.ReadAsync(token))
            {
                var id = reader.GetGuid(0);
                var key = MarketingCodeRules.Normalize(reader.GetString(1));
                if (!keys.TryAdd(key, id))
                    throw new InvalidMigrationException("Обнаружены кампании с совпадающими ключами промокодов. Требуется ручное разрешение коллизий.");
                rows.Add((id, key));
            }
        }

        // No code value or row has been changed before collision preflight succeeds.
        await SqlMigration.RunAsync("ALTER TABLE public.marketing_campaigns ADD COLUMN normalized_code text COLLATE \"C\"", context);
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            await using var update = new NpgsqlCommand("UPDATE public.marketing_campaigns SET normalized_code = @key WHERE id = @id", context.Connection, context.Transaction);
            update.Parameters.AddWithValue("key", row.Key);
            update.Parameters.AddWithValue("id", row.Id);
            await update.ExecuteNonQueryAsync(token);
        }
        await SqlMigration.RunAsync("""
            ALTER TABLE public.marketing_campaigns
                ALTER COLUMN normalized_code SET NOT NULL,
                ADD CONSTRAINT ck_marketing_campaigns_normalized_code CHECK (normalized_code <> ''),
                ADD CONSTRAINT ux_marketing_campaigns_normalized_code UNIQUE(normalized_code);
            """, context);
    }
}
