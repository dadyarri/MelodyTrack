using System.Data;
using System.Security.Cryptography;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;
using MelodyTrack.Rewrite.Persistence.Clients;
using MelodyTrack.Rewrite.Persistence.Import;
using Npgsql;

namespace MelodyTrack.Rewrite.Init.Import;

public static class ClientImport
{
    private sealed record Source(byte[] Id, string Name, int Row);
    private sealed record OldContact(byte[] Id, string?[] Values, int Row);
    private sealed record OldClient(byte[] Id, string First, string Last, string? Middle, byte[] ContactsId,
        byte[]? SourceId, string? Birthdate, DateOnly? Date, string CreatedAtText, DateTimeOffset CreatedAt, bool Closed, int Row);
    private sealed record ImportedContact(ClientContact Model, string Field);
    private static readonly (string Field, string Name, ContactFormat Format, string Label)[] Fields =
    [
        ("Phone", "Телефон", ContactFormat.Phone, "phone"),
        ("Email", "Email", ContactFormat.Email, "email"),
        ("Telegram", "Telegram", ContactFormat.Link, "link"),
        ("Vk", "VK", ContactFormat.Link, "link")
    ];

    public static async Task<ClientImportReport> RunAsync(NpgsqlConnection source, NpgsqlConnection target,
        LegacyPersonalDataReader legacy, ClientDataProtection protection, CancellationToken cancellationToken)
    {
        // The source is never passed to MigrationRunner or to any target-writing helper.
        await using var snapshot = await source.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await Execute(source, snapshot, "SET TRANSACTION READ ONLY", cancellationToken);
        await Execute(source, snapshot, "SET LOCAL TIME ZONE 'UTC'", cancellationToken);
        await Execute(source, snapshot, "SET LOCAL DateStyle = 'ISO, YMD'", cancellationToken);
        var sources = await ReadSources(source, snapshot, cancellationToken);
        var contacts = await ReadContacts(source, snapshot, cancellationToken);
        var clients = await ReadClients(source, snapshot, cancellationToken);
        var report = new ClientImportReport { SourceRows = sources.Count, ClientRows = clients.Count, ContactRows = contacts.Count };

        // Reject an old installation even if callers bypass the ordinary Init entry point.
        await using (var guard = new NpgsqlCommand("SELECT to_regclass('public.\"__EFMigrationsHistory\"') IS NOT NULL", target))
            if (await guard.ExecuteScalarAsync(cancellationToken) is true)
                throw new InvalidMigrationException("Импорт требует отдельной целевой базы.");

        await using var transaction = await target.BeginTransactionAsync(cancellationToken);
        await Execute(target, transaction, "SELECT pg_advisory_xact_lock(5572151795415338320)", cancellationToken);
        await Execute(target, transaction, "SET LOCAL TIME ZONE 'UTC'", cancellationToken);
        await Execute(target, transaction, "SET LOCAL DateStyle = 'ISO, YMD'", cancellationToken);
        var sourceIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var row in sources)
        {
            if (!ValidId(row.Id) || string.IsNullOrWhiteSpace(row.Name))
            {
                report.Issues.Add(new("ClientSources", row.Row, null, "invalid_source"));
                continue;
            }
            await Execute(target, transaction, "SAVEPOINT import_row", cancellationToken);
            var id = await MapId(target, transaction, "ClientSources", row.Id, "", cancellationToken);
            var existing = await Scalar(target, transaction, "SELECT name FROM public.client_sources WHERE id = $1", cancellationToken, id);
            if (existing is string name && name != row.Name)
            {
                report.Issues.Add(new("ClientSources", row.Row, "Name", "target_source_differs"));
                await Execute(target, transaction, "ROLLBACK TO SAVEPOINT import_row", cancellationToken);
            }
            else
            {
                if (existing is null)
                {
                    await Execute(target, transaction, "INSERT INTO public.client_sources(id, name) VALUES ($1, $2)", cancellationToken, id, row.Name);
                    report.SourcesInserted++;
                }
                else report.SourcesMatched++;
                sourceIds.Add(Convert.ToHexString(row.Id), id);
            }
            await Execute(target, transaction, "RELEASE SAVEPOINT import_row", cancellationToken);
        }

        var kinds = await ContactKinds(target, transaction, cancellationToken);
        var contactsById = contacts.ToDictionary(row => Convert.ToHexString(row.Id), StringComparer.Ordinal);
        var owners = clients.Select(row => Convert.ToHexString(row.ContactsId)).ToHashSet(StringComparer.Ordinal);
        foreach (var orphan in contacts.Where(row => !owners.Contains(Convert.ToHexString(row.Id))))
            report.Issues.Add(new("ClientContacts", orphan.Row, null, "orphan_contact"));

        foreach (var row in clients)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Execute(target, transaction, "SAVEPOINT import_row", cancellationToken);
            var previousIssueCount = report.Issues.Count;
            if (!ValidId(row.Id) || !ValidId(row.ContactsId))
                report.Issues.Add(new("Clients", row.Row, null, "invalid_identifier"));
            Guid? sourceId = null;
            if (row.SourceId is not null)
            {
                if (sourceIds.TryGetValue(Convert.ToHexString(row.SourceId), out var mapped)) sourceId = mapped;
                else report.Issues.Add(new("Clients", row.Row, "SourceId", "unresolved_source"));
            }
            if (!contactsById.TryGetValue(Convert.ToHexString(row.ContactsId), out var contactRow))
                report.Issues.Add(new("Clients", row.Row, "ContactsId", "missing_contact_row"));
            else if (!ValidId(contactRow.Id))
                report.Issues.Add(new("Clients", row.Row, "ContactsId", "invalid_identifier"));
            var name = PersonName.Create(row.First, row.Last, row.Middle);
            if (name.IsFailure || name.Value.First != row.First || name.Value.Last != row.Last || name.Value.Middle != row.Middle)
                report.Issues.Add(new("Clients", row.Row, "Name", "name_requires_manual_resolution"));

            if (report.Issues.Count == previousIssueCount)
            {
                var clientId = new ClientId(await MapId(target, transaction, "Clients", row.Id, "", cancellationToken));
                var values = new List<ImportedContact>();
                for (var index = 0; index < Fields.Length; index++)
                {
                    var field = Fields[index];
                    string? value;
                    try { value = legacy.Read(contactRow!.Values[index]); }
                    catch (CryptographicException)
                    {
                        report.Issues.Add(new("Clients", row.Row, field.Field, "source_decryption_failed"));
                        continue;
                    }
                    if (string.IsNullOrWhiteSpace(value)) continue;
                    var part = Convert.ToHexString(row.Id) + ":" + field.Field;
                    var id = new ClientContactId(await MapId(target, transaction, "ClientContacts", contactRow!.Id, part, cancellationToken));
                    var restored = ClientContactRules.Restore(id, kinds[index], value, null, true);
                    if (restored.IsFailure)
                        report.Issues.Add(new("Clients", row.Row, field.Field, "invalid_contact_value"));
                    else values.Add(new(restored.Value, field.Field));
                }

                if (report.Issues.Count == previousIssueCount)
                {
                    var profile = new ClientProfile(name.Value, row.Date, sourceId is { } known ? new ClientSourceId(known) : null, null);
                    var restored = ClientRules.Restore(clientId, profile, values.Select(value => value.Model), row.CreatedAt, row.Closed);
                    if (restored.IsFailure)
                        report.Issues.Add(new("Clients", row.Row, null, "invalid_client_state"));
                    else
                    {
                        var exists = await Scalar(target, transaction, "SELECT 1 FROM public.clients WHERE id = $1", cancellationToken, clientId.Value) is not null;
                        if (!exists) await WriteClient(target, transaction, row, restored.Value, values, protection, cancellationToken);
                        bool matches;
                        try { matches = await Matches(target, transaction, row, restored.Value, values, protection, cancellationToken); }
                        catch (CryptographicException) { matches = false; }
                        if (!matches) report.Issues.Add(new("Clients", row.Row, null, "target_client_differs"));
                        else if (exists) report.ClientsMatched++;
                        else
                        {
                            report.ClientsInserted++;
                            report.ContactsInserted += values.Count;
                        }
                    }
                }
            }
            if (report.Issues.Count != previousIssueCount)
                await Execute(target, transaction, "ROLLBACK TO SAVEPOINT import_row", cancellationToken);
            await Execute(target, transaction, "RELEASE SAVEPOINT import_row", cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        await snapshot.RollbackAsync(cancellationToken);
        return report;
    }

    private static bool ValidId(byte[] id) => id.Length == 16 && id.Any(value => value != 0);

    private static async Task<Guid> MapId(NpgsqlConnection connection, NpgsqlTransaction transaction,
        string table, byte[] oldId, string part, CancellationToken token)
    {
        var existing = await Scalar(connection, transaction,
            "SELECT target_id FROM public.import_id_map WHERE source_table=$1 AND source_id=$2 AND part=$3", token, table, oldId, part);
        if (existing is Guid id) return id;
        var created = Guid.CreateVersion7();
        await Execute(connection, transaction,
            "INSERT INTO public.import_id_map(source_table, source_id, part, target_id) VALUES ($1,$2,$3,$4)", token, table, oldId, part, created);
        return created;
    }

    private static async Task<ContactKind[]> ContactKinds(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        var kinds = new List<ContactKind>();
        foreach (var field in Fields)
        {
            var existing = await Scalar(connection, transaction, "SELECT kind_id FROM public.import_contact_kinds WHERE source_field=$1", token, field.Field);
            var id = existing is Guid known ? known : Guid.CreateVersion7();
            if (existing is null)
            {
                await Execute(connection, transaction, "INSERT INTO public.contact_kinds(id,name,format) VALUES ($1,$2,CAST($3 AS public.contact_format))", token, id, field.Name, field.Label);
                await Execute(connection, transaction, "INSERT INTO public.import_contact_kinds(source_field,kind_id) VALUES ($1,$2)", token, field.Field, id);
            }
            var format = await Scalar(connection, transaction, "SELECT format::text FROM public.contact_kinds WHERE id=$1", token, id);
            if (!Equals(format, field.Label)) throw new InvalidMigrationException("Изменён формат импортного вида контактов.");
            var kind = ContactKindRules.Create(new(id), field.Name, field.Format).Value;
            kinds.Add(kind);
        }
        return kinds.ToArray();
    }

    private static async Task WriteClient(NpgsqlConnection connection, NpgsqlTransaction transaction, OldClient old, Client client,
        List<ImportedContact> contacts, ClientDataProtection protection, CancellationToken token)
    {
        await Execute(connection, transaction, """
            INSERT INTO public.clients(id,first_name,last_name,middle_name,birthdate,source_id,protected_notes,created_at,is_archived)
            VALUES ($1,$2,$3,$4,CAST($5 AS date),$6,$7,CAST($8 AS timestamptz),$9)
            """, token, client.Id.Value, old.First, old.Last, Db(old.Middle), Db(old.Birthdate), Db(client.Profile.SourceId?.Value),
            Db(protection.ProtectNotes(client.Id, client.Profile.Notes)), old.CreatedAtText, client.IsArchived);
        foreach (var contact in contacts)
            await Execute(connection, transaction, """
                INSERT INTO public.client_contacts(id,client_id,kind_id,protected_value,protected_comment,is_primary)
                VALUES ($1,$2,$3,$4,$5,true)
                """, token, contact.Model.Id.Value, client.Id.Value, contact.Model.KindId.Value,
                protection.ProtectValue(client.Id, contact.Model.Id, contact.Model.Value),
                Db(protection.ProtectComment(client.Id, contact.Model.Id, contact.Model.Comment)));
    }

    private static async Task<bool> Matches(NpgsqlConnection connection, NpgsqlTransaction transaction, OldClient old, Client client,
        List<ImportedContact> contacts, ClientDataProtection protection, CancellationToken token)
    {
        await using (var command = Command(connection, transaction, """
            SELECT first_name,last_name,middle_name,birthdate::text,source_id,created_at::text,is_archived,protected_notes
            FROM public.clients WHERE id=$1
            """, client.Id.Value))
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) return false;
            if (reader.GetString(0) != old.First || reader.GetString(1) != old.Last || Text(reader, 2) != old.Middle
                || Text(reader, 3) != old.Birthdate || (reader.IsDBNull(4) ? (Guid?)null : reader.GetGuid(4)) != client.Profile.SourceId?.Value
                || reader.GetString(5) != old.CreatedAtText || reader.GetBoolean(6) != client.IsArchived
                || protection.UnprotectNotes(client.Id, Text(reader, 7)) != client.Profile.Notes) return false;
        }
        var expected = contacts.ToDictionary(contact => contact.Model.Id.Value);
        await using var rows = Command(connection, transaction,
            "SELECT id,kind_id,protected_value,protected_comment,is_primary FROM public.client_contacts WHERE client_id=$1", client.Id.Value);
        await using var result = await rows.ExecuteReaderAsync(token);
        var count = 0;
        while (await result.ReadAsync(token))
        {
            count++;
            if (!expected.TryGetValue(result.GetGuid(0), out var contact) || result.GetGuid(1) != contact.Model.KindId.Value
                || !result.GetBoolean(4) || protection.UnprotectValue(client.Id, contact.Model.Id, result.GetString(2)) != contact.Model.Value
                || protection.UnprotectComment(client.Id, contact.Model.Id, Text(result, 3)) != contact.Model.Comment) return false;
        }
        return count == expected.Count;
    }

    private static async Task<List<Source>> ReadSources(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT \"Id\",\"Name\" FROM public.\"ClientSources\" ORDER BY \"Id\"");
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<Source>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetFieldValue<byte[]>(0), reader.GetString(1), rows.Count + 1));
        return rows;
    }

    private static async Task<List<OldContact>> ReadContacts(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        await using var command = Command(connection, transaction, "SELECT \"Id\",\"Phone\",\"Email\",\"Telegram\",\"Vk\" FROM public.\"ClientContacts\" ORDER BY \"Id\"");
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<OldContact>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetFieldValue<byte[]>(0), [Text(reader, 1), Text(reader, 2), Text(reader, 3), Text(reader, 4)], rows.Count + 1));
        return rows;
    }

    private static async Task<List<OldClient>> ReadClients(NpgsqlConnection connection, NpgsqlTransaction transaction, CancellationToken token)
    {
        await using var command = Command(connection, transaction, """
            SELECT "Id","FirstName","LastName","Patronymic","ContactsId","SourceId","DateOfBirth"::text,"DateOfBirth",
                   "CreatedAtUtc"::text,"CreatedAtUtc","IsLeadClosed" FROM public."Clients" ORDER BY "Id"
            """);
        await using var reader = await command.ExecuteReaderAsync(token);
        var rows = new List<OldClient>();
        while (await reader.ReadAsync(token)) rows.Add(new(reader.GetFieldValue<byte[]>(0), reader.GetString(1), reader.GetString(2), Text(reader, 3),
            reader.GetFieldValue<byte[]>(4), reader.IsDBNull(5) ? null : reader.GetFieldValue<byte[]>(5), Text(reader, 6),
            reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7), reader.GetString(8),
            new DateTimeOffset(DateTime.SpecifyKind(reader.GetDateTime(9), DateTimeKind.Utc)), reader.GetBoolean(10), rows.Count + 1));
        return rows;
    }

    private static string? Text(NpgsqlDataReader reader, int ordinal) => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    private static object Db(object? value) => value ?? DBNull.Value;

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, params object[] values)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values)
        {
            if (value is DBNull) command.Parameters.Add(new NpgsqlParameter { Value = DBNull.Value, NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Unknown });
            else command.Parameters.AddWithValue(value);
        }
        return command;
    }

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token, params object[] values)
    {
        await using var command = Command(connection, transaction, sql, values);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<object?> Scalar(NpgsqlConnection connection, NpgsqlTransaction transaction, string sql, CancellationToken token, params object[] values)
    {
        await using var command = Command(connection, transaction, sql, values);
        var result = await command.ExecuteScalarAsync(token);
        return result is DBNull ? null : result;
    }
}
