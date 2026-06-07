using System.Data;
using MelodyTrack.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public interface IPersonalDataBackfillService
{
    Task BackfillAsync(CancellationToken cancellationToken);
}

public sealed class PersonalDataBackfillService(AppDbContext db, IPersonalDataProtector protector) : IPersonalDataBackfillService
{
    public async Task BackfillAsync(CancellationToken cancellationToken)
    {
        await BackfillUsersAsync(cancellationToken);
        await BackfillClientContactsAsync(cancellationToken);
    }

    private async Task BackfillUsersAsync(CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT "Id", "Phone", "Telegram", "Vk"
                                  FROM public."Users"
                                  WHERE "Phone" IS NOT NULL OR "Telegram" IS NOT NULL OR "Vk" IS NOT NULL
                                  """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<UserBackfillRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new UserBackfillRow(
                    (byte[])reader["Id"],
                    reader["Phone"] as string,
                    reader["Telegram"] as string,
                    reader["Vk"] as string));
            }

            await reader.CloseAsync();

            foreach (var row in rows)
            {
                await UpdateIfNeededAsync(
                    connection,
                    """
                    UPDATE public."Users"
                    SET "Phone" = @phone, "Telegram" = @telegram, "Vk" = @vk
                    WHERE "Id" = @id
                    """,
                    row.Id,
                    row.Phone,
                    row.Telegram,
                    row.Vk,
                    cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task BackfillClientContactsAsync(CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State != ConnectionState.Open;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                                  SELECT "Id", "Phone", "Telegram", "Vk"
                                  FROM public."ClientContacts"
                                  WHERE "Phone" IS NOT NULL OR "Telegram" IS NOT NULL OR "Vk" IS NOT NULL
                                  """;

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var rows = new List<ClientContactsBackfillRow>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(new ClientContactsBackfillRow(
                    (byte[])reader["Id"],
                    reader["Phone"] as string,
                    reader["Telegram"] as string,
                    reader["Vk"] as string));
            }

            await reader.CloseAsync();

            foreach (var row in rows)
            {
                await UpdateIfNeededAsync(
                    connection,
                    """
                    UPDATE public."ClientContacts"
                    SET "Phone" = @phone, "Telegram" = @telegram, "Vk" = @vk
                    WHERE "Id" = @id
                    """,
                    row.Id,
                    row.Phone,
                    row.Telegram,
                    row.Vk,
                    cancellationToken);
            }
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync();
            }
        }
    }

    private async Task UpdateIfNeededAsync(
        System.Data.Common.DbConnection connection,
        string sql,
        byte[] id,
        string? phone,
        string? telegram,
        string? vk,
        CancellationToken cancellationToken)
    {
        var nextPhone = EncryptIfNeeded(phone);
        var nextTelegram = EncryptIfNeeded(telegram);
        var nextVk = EncryptIfNeeded(vk);

        if (nextPhone == phone && nextTelegram == telegram && nextVk == vk)
        {
            return;
        }

        await using var update = connection.CreateCommand();
        update.CommandText = sql;

        AddParameter(update, "id", id);
        AddParameter(update, "phone", (object?)nextPhone ?? DBNull.Value);
        AddParameter(update, "telegram", (object?)nextTelegram ?? DBNull.Value);
        AddParameter(update, "vk", (object?)nextVk ?? DBNull.Value);

        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private string? EncryptIfNeeded(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || protector.IsEncrypted(value))
        {
            return value;
        }

        return protector.Encrypt(value);
    }

    private static void AddParameter(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private sealed record UserBackfillRow(byte[] Id, string? Phone, string? Telegram, string? Vk);
    private sealed record ClientContactsBackfillRow(byte[] Id, string? Phone, string? Telegram, string? Vk);
}
