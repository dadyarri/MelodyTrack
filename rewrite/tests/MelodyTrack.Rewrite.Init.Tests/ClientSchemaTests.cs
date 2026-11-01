using System.Data;
using System.Security.Cryptography;
using System.Text;
using MelodyTrack.Rewrite.Init;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class ClientSchemaTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly Guid ClientId = Guid.Parse("019a2c63-8c00-7000-8000-000000000001");
    private static readonly Guid KindId = Guid.Parse("019a2c63-8c00-7000-8000-000000000002");
    private static readonly Guid FirstContactId = Guid.Parse("019a2c63-8c00-7000-8000-000000000003");
    private static readonly Guid SecondContactId = Guid.Parse("019a2c63-8c00-7000-8000-000000000004");

    private async Task WithDatabase(Func<NpgsqlConnection, Task> scenario)
    {
        var connectionString = await fixture.CreateDatabaseAsync(Token);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await MigrationRunner.PrepareAsync(connection, Token);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Schema", "0001_CreateClients.sql"), Token);
        var migration = new Migration(1, "0001_CreateClients.sql", Convert.ToHexString(SHA256.HashData(bytes)),
            context => SqlMigration.RunAsync(Encoding.UTF8.GetString(bytes), context));
        await MigrationRunner.ApplyAsync(connection, migration, NullLogger.Instance, Token);
        await scenario(connection);
    }

    private static async Task Execute(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<long> CountClients(NpgsqlConnection connection)
    {
        await using var command = new NpgsqlCommand("SELECT count(*) FROM public.clients", connection);
        return (long)(await command.ExecuteScalarAsync(Token))!;
    }

    private static Task AddClient(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id) => Execute(connection, transaction,
        "INSERT INTO public.clients (id, first_name, last_name, birthdate, created_at, is_lead_closed) VALUES ($1, 'First', 'Last', '2000-02-29', '2025-10-01T12:00:00Z', true)", id);

    private static Task AddKind(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id) => Execute(connection, transaction,
        "INSERT INTO public.contact_kinds (id, name, format) VALUES ($1, 'Phone', 'phone')", id);

    private static Task AddContact(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid id, Guid client, Guid kind, bool primary) =>
        Execute(connection, transaction,
            "INSERT INTO public.client_contacts (id, client_id, kind_id, protected_value, is_primary) VALUES ($1, $2, $3, 'opaque-test-payload', $4)",
            id, client, kind, primary);

    private static async Task SeedClient(NpgsqlConnection connection, bool secondKind = false)
    {
        await using var transaction = await connection.BeginTransactionAsync(Token);
        await AddKind(connection, transaction, KindId);
        await AddClient(connection, transaction, ClientId);
        await AddContact(connection, transaction, FirstContactId, ClientId, KindId, true);
        if (secondKind)
        {
            var otherKind = Guid.Parse("019a2c63-8c00-7000-8000-000000000005");
            await AddKind(connection, transaction, otherKind);
            await AddContact(connection, transaction, SecondContactId, ClientId, otherKind, true);
        }
        else
        {
            await AddContact(connection, transaction, SecondContactId, ClientId, KindId, false);
        }
        await transaction.CommitAsync(Token);
    }

    [Fact]
    public Task Commit_HistoricalClientWithoutSource_PreservesBirthdateAndMetadata() => WithDatabase(async connection =>
    {
        await SeedClient(connection);

        await using var command = new NpgsqlCommand("SELECT source_id, created_at, is_lead_closed, is_archived, birthdate FROM public.clients WHERE id = $1", connection);
        command.Parameters.AddWithValue(ClientId);
        await using var reader = await command.ExecuteReaderAsync(Token);

        Assert.True(await reader.ReadAsync(Token));
        Assert.True(reader.IsDBNull(0));
        Assert.Equal(new DateTime(2025, 10, 1, 12, 0, 0, DateTimeKind.Utc), reader.GetDateTime(1));
        Assert.True(reader.GetBoolean(2));
        Assert.False(reader.GetBoolean(3));
        Assert.Equal(new DateOnly(2000, 2, 29), reader.GetFieldValue<DateOnly>(4));
    });

    [Fact]
    public Task Commit_NoContacts_SavesClient() => WithDatabase(async connection =>
    {
        await using (var transaction = await connection.BeginTransactionAsync(Token))
        {
            await AddClient(connection, transaction, ClientId);

            await transaction.CommitAsync(Token);
        }

        Assert.Equal(1L, await CountClients(connection));
    });

    [Fact]
    public Task Commit_KindWithoutPrimary_RejectsAggregate() => WithDatabase(async connection =>
    {
        PostgresException error;
        await using (var transaction = await connection.BeginTransactionAsync(Token))
        {
            await AddKind(connection, transaction, KindId);
            await AddClient(connection, transaction, ClientId);
            await AddContact(connection, transaction, FirstContactId, ClientId, KindId, false);

            error = await Assert.ThrowsAsync<PostgresException>(() => transaction.CommitAsync(Token));
        }

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        Assert.Equal("ck_client_contacts_primary_required", error.ConstraintName);
        Assert.Equal(0L, await CountClients(connection));
    });

    [Fact]
    public Task Insert_TwoPrimariesForSameKind_RejectsDuplicatePrimary() => WithDatabase(async connection =>
    {
        await SeedClient(connection);
        await using var transaction = await connection.BeginTransactionAsync(Token);

        var error = await Assert.ThrowsAsync<PostgresException>(() => Execute(connection, transaction,
            "UPDATE public.client_contacts SET is_primary = true WHERE id = $1", SecondContactId));

        Assert.Equal(PostgresErrorCodes.UniqueViolation, error.SqlState);
        Assert.Equal("ux_client_contacts_primary", error.ConstraintName);
    });

    [Fact]
    public Task Commit_ChangingPrimary_AllowsTemporaryGap() => WithDatabase(async connection =>
    {
        await SeedClient(connection);
        await using (var transaction = await connection.BeginTransactionAsync(Token))
        {
            await Execute(connection, transaction, "UPDATE public.client_contacts SET is_primary = false WHERE id = $1", FirstContactId);
            await Execute(connection, transaction, "UPDATE public.client_contacts SET is_primary = true WHERE id = $1", SecondContactId);

            await transaction.CommitAsync(Token);
        }

        await using var command = new NpgsqlCommand("SELECT id FROM public.client_contacts WHERE client_id = $1 AND is_primary", connection);
        command.Parameters.AddWithValue(ClientId);
        Assert.Equal(SecondContactId, (Guid)(await command.ExecuteScalarAsync(Token))!);
    });

    [Fact]
    public Task Commit_RemovingAllContacts_PreservesClientWithEmptyContacts() => WithDatabase(async connection =>
    {
        await SeedClient(connection);
        await using (var transaction = await connection.BeginTransactionAsync(Token))
        {
            await Execute(connection, transaction, "DELETE FROM public.client_contacts WHERE client_id = $1", ClientId);

            await transaction.CommitAsync(Token);
        }

        Assert.Equal(1L, await CountClients(connection));
        await using var command = new NpgsqlCommand("SELECT count(*) FROM public.client_contacts WHERE client_id = $1", connection);
        command.Parameters.AddWithValue(ClientId);
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(Token))!);
    });

    [Fact]
    public Task Delete_LastContactOfOneKind_PreservesOtherKindAndItsPrimary() => WithDatabase(async connection =>
    {
        await SeedClient(connection, secondKind: true);

        await Execute(connection, null, "DELETE FROM public.client_contacts WHERE id = $1", FirstContactId);

        await using var command = new NpgsqlCommand("SELECT id, is_primary FROM public.client_contacts WHERE client_id = $1", connection);
        command.Parameters.AddWithValue(ClientId);
        await using var reader = await command.ExecuteReaderAsync(Token);
        Assert.True(await reader.ReadAsync(Token));
        Assert.Equal(SecondContactId, reader.GetGuid(0));
        Assert.True(reader.GetBoolean(1));
        Assert.False(await reader.ReadAsync(Token));
    });

    [Fact]
    public Task Commit_SameContactValueForDifferentClients_DoesNotMergeOrRejectClients() => WithDatabase(async connection =>
    {
        await SeedClient(connection);
        var otherClient = Guid.Parse("019a2c63-8c00-7000-8000-000000000006");
        var otherContact = Guid.Parse("019a2c63-8c00-7000-8000-000000000007");
        await using (var transaction = await connection.BeginTransactionAsync(Token))
        {
            await AddClient(connection, transaction, otherClient);
            await AddContact(connection, transaction, otherContact, otherClient, KindId, true);

            await transaction.CommitAsync(Token);
        }

        Assert.Equal(2L, await CountClients(connection));
    });

    [Fact]
    public Task Delete_ClientAggregate_AllowsOwnedContactsToBeRemoved() => WithDatabase(async connection =>
    {
        await SeedClient(connection);

        await Execute(connection, null, "DELETE FROM public.clients WHERE id = $1", ClientId);

        Assert.Equal(0L, await CountClients(connection));
    });

    [Fact]
    public Task Insert_StaleRepeatableReadSnapshot_PreventsKindWithoutPrimary() => WithDatabase(async connection =>
    {
        await SeedClient(connection);
        await using var staleConnection = new NpgsqlConnection(fixture.ConnectionString(connection.Database));
        await staleConnection.OpenAsync(Token);
        await using var staleTransaction = await staleConnection.BeginTransactionAsync(IsolationLevel.RepeatableRead, Token);
        await Execute(staleConnection, staleTransaction, "SELECT id FROM public.clients WHERE id = $1", ClientId);
        await using (var transaction = await connection.BeginTransactionAsync(Token))
        {
            await Execute(connection, transaction, "DELETE FROM public.client_contacts WHERE client_id = $1", ClientId);
            await transaction.CommitAsync(Token);
        }

        var newContactId = Guid.Parse("019a2c63-8c00-7000-8000-000000000008");

        var error = await Assert.ThrowsAsync<PostgresException>(() => AddContact(staleConnection, staleTransaction,
            newContactId, ClientId, KindId, false));

        Assert.Equal(PostgresErrorCodes.SerializationFailure, error.SqlState);
    });
}
