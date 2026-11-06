using System.Security.Cryptography;
using System.Text;
using MelodyTrack.Rewrite.Init.Import;
using MelodyTrack.Rewrite.Persistence.Clients;
using MelodyTrack.Rewrite.Persistence.Import;
using MelodyTrack.Rewrite.Persistence.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class ClientImportTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    internal const string LegacyKey = "legacy-test-only-master-key-32-characters";
    internal const string EncryptedPhone = "enc:old:AAECAwQFBgcICQoL:x0b/a+KghRvLCsiTyTbRBzRd98w=:G1Y95tIjCIKiQMqMq5VLxw==";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static PersonalDataProtector Protector() => new("test", new Dictionary<string, string>
    {
        ["test"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
    });

    internal static async Task ApplySchema(NpgsqlConnection connection)
    {
        await MigrationRunner.PrepareAsync(connection, Token);
        var names = new[] { "0001_CreateClients.sql", "0002_CreateImportMappings.sql" };
        for (var index = 0; index < names.Length; index++)
        {
            var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "Schema", names[index]), Token);
            await MigrationRunner.ApplyAsync(connection,
                new(index + 1, names[index], Convert.ToHexString(SHA256.HashData(bytes)), context => SqlMigration.RunAsync(Encoding.UTF8.GetString(bytes), context)),
                NullLogger.Instance, Token);
        }
    }

    private async Task WithDatabases(Func<NpgsqlConnection, NpgsqlConnection, Task> scenario)
    {
        await using var source = new NpgsqlConnection(await fixture.CreateDatabaseAsync(Token));
        await source.OpenAsync(Token);
        await Execute(source, """
            CREATE TABLE public."__EFMigrationsHistory" ("MigrationId" text PRIMARY KEY);
            CREATE TABLE public."ClientSources" ("Id" bytea PRIMARY KEY, "Name" text NOT NULL);
            CREATE TABLE public."ClientContacts" ("Id" bytea PRIMARY KEY, "Phone" text, "Email" text, "Telegram" text, "Vk" text);
            CREATE TABLE public."Clients" ("Id" bytea PRIMARY KEY, "FirstName" text NOT NULL, "LastName" text NOT NULL,
                "Patronymic" text, "ContactsId" bytea NOT NULL, "SourceId" bytea, "DateOfBirth" date,
                "CreatedAtUtc" timestamptz NOT NULL, "IsLeadClosed" boolean NOT NULL);
            INSERT INTO public."ClientSources" VALUES (decode(repeat('11',16),'hex'), 'Existing source');
            INSERT INTO public."ClientContacts" ("Id","Email") VALUES (decode(repeat('21',16),'hex'), 'parent@example.test');
            INSERT INTO public."ClientContacts" ("Id") VALUES (decode(repeat('22',16),'hex'));
            INSERT INTO public."Clients" VALUES
                (decode(repeat('01',16),'hex'),'First','Last','Middle',decode(repeat('21',16),'hex'),decode(repeat('11',16),'hex'),'2000-02-29','2025-10-01T12:00:00Z',false),
                (decode(repeat('02',16),'hex'),'Another','Client',null,decode(repeat('21',16),'hex'),null,null,'2025-10-02T12:00:00Z',true),
                (decode(repeat('03',16),'hex'),'Empty','Contacts',null,decode(repeat('22',16),'hex'),null,null,'-infinity',false);
            """);
        await using (var phone = new NpgsqlCommand("UPDATE public.\"ClientContacts\" SET \"Phone\"=$1 WHERE \"Id\"=decode(repeat('21',16),'hex')", source))
        {
            phone.Parameters.AddWithValue(EncryptedPhone);
            await phone.ExecuteNonQueryAsync(Token);
        }
        await using var target = new NpgsqlConnection(await fixture.CreateDatabaseAsync(Token));
        await target.OpenAsync(Token);
        await ApplySchema(target);
        await scenario(source, target);
    }

    [Fact]
    public Task Run_SharedContactsAndEmptyClient_PreservesStateAndRepeatsWithoutDuplicates() => WithDatabases(async (source, target) =>
    {
        using var legacy = new LegacyPersonalDataReader(new Dictionary<string, string> { ["old"] = LegacyKey });
        using var protector = Protector();
        var fields = new ClientDataProtection(protector);

        var first = await ClientImport.RunAsync(source, target, legacy, fields, Token);
        var second = await ClientImport.RunAsync(source, target, legacy, fields, Token);

        Assert.True(first.Complete);
        Assert.Equal(3, first.ClientsInserted);
        Assert.Equal(4, first.ContactsInserted);
        Assert.True(second.Complete);
        Assert.Equal(0, second.ClientsInserted);
        Assert.Equal(0, second.ContactsInserted);
        Assert.Equal(3, second.ClientsMatched);
        Assert.Equal(1, second.SourcesMatched);
        Assert.Equal(8L, await Scalar<long>(target, "SELECT count(*) FROM public.import_id_map"));
        Assert.Equal(2L, await Scalar<long>(target, "SELECT count(*) FROM public.clients WHERE source_id IS NULL"));
        Assert.Equal(1L, await Scalar<long>(target, "SELECT count(*) FROM public.clients WHERE created_at='-infinity'::timestamptz"));
        Assert.Equal(1L, await Scalar<long>(target, "SELECT count(*) FROM public.clients WHERE birthdate='2000-02-29'"));
        Assert.Equal(1L, await Scalar<long>(target, "SELECT count(*) FROM public.clients WHERE is_lead_closed"));
        Assert.Equal(0L, await Scalar<long>(target, "SELECT count(*) FROM public.clients WHERE is_archived"));
        Assert.Equal(0L, await Scalar<long>(target, "SELECT count(*) FROM public.client_contacts WHERE protected_value NOT LIKE 'mtpii:1:%'"));
        Assert.Equal(3L, await Scalar<long>(source, "SELECT count(*) FROM public.\"Clients\""));
    });

    [Fact]
    public Task Run_DifferentSessionDateSettings_PreservesDateAndInstant() => WithDatabases(async (source, target) =>
    {
        await Execute(source, "SET DateStyle='SQL, DMY'; SET TIME ZONE 'Asia/Yekaterinburg'");
        await Execute(target, "SET DateStyle='SQL, MDY'; SET TIME ZONE 'America/Los_Angeles'");
        using var legacy = new LegacyPersonalDataReader(new Dictionary<string, string> { ["old"] = LegacyKey });
        using var protector = Protector();

        var result = await ClientImport.RunAsync(source, target, legacy, new(protector), Token);

        Assert.True(result.Complete);
        Assert.Equal(3, result.ClientsInserted);
        Assert.Equal(1L, await Scalar<long>(target, "SELECT count(*) FROM public.clients WHERE birthdate=make_date(2000,2,29) AND created_at='2025-10-01T12:00:00Z'::timestamptz"));
    });

    [Fact]
    public Task Run_OrphanRow_ReportsWithoutAssigningItToClient() => WithDatabases(async (source, target) =>
    {
        await Execute(source, "INSERT INTO public.\"ClientContacts\" (\"Id\",\"Phone\") VALUES (decode(repeat('23',16),'hex'), '+1 202 555 0100')");
        using var legacy = new LegacyPersonalDataReader(new Dictionary<string, string> { ["old"] = LegacyKey });
        using var protector = Protector();

        var result = await ClientImport.RunAsync(source, target, legacy, new(protector), Token);

        Assert.False(result.Complete);
        Assert.Equal("orphan_contact", Assert.Single(result.Issues).Code);
        Assert.Equal(3, result.ClientsInserted);
        Assert.Equal(4, result.ContactsInserted);
    });

    [Fact]
    public Task Run_UnreadableContact_SkipsWholeClientAndItsMappings() => WithDatabases(async (source, target) =>
    {
        using var legacy = new LegacyPersonalDataReader(new Dictionary<string, string>());
        using var protector = Protector();

        var result = await ClientImport.RunAsync(source, target, legacy, new(protector), Token);

        Assert.Equal(1, result.ClientsInserted);
        Assert.Equal(0, result.ContactsInserted);
        Assert.Equal(2, result.Issues.Count);
        Assert.All(result.Issues, issue => Assert.Equal("source_decryption_failed", issue.Code));
        Assert.Equal(2L, await Scalar<long>(target, "SELECT count(*) FROM public.import_id_map"));
    });

    [Fact]
    public Task Run_NonUrlLink_RequiresManualResolutionWithoutInventingUrl() => WithDatabases(async (source, target) =>
    {
        await Execute(source, "UPDATE public.\"ClientContacts\" SET \"Telegram\"='@unrepresentable' WHERE \"Id\"=decode(repeat('21',16),'hex')");
        using var legacy = new LegacyPersonalDataReader(new Dictionary<string, string> { ["old"] = LegacyKey });
        using var protector = Protector();

        var result = await ClientImport.RunAsync(source, target, legacy, new(protector), Token);

        Assert.Equal(1, result.ClientsInserted);
        Assert.All(result.Issues, issue => Assert.Equal("invalid_contact_value", issue.Code));
        Assert.Equal(2, result.Issues.Count);
        Assert.Equal(0L, await Scalar<long>(target, "SELECT count(*) FROM public.client_contacts"));
    });

    [Fact]
    public Task Run_ChangedTarget_ReportsDifferenceInsteadOfOverwritingIt() => WithDatabases(async (source, target) =>
    {
        using var legacy = new LegacyPersonalDataReader(new Dictionary<string, string> { ["old"] = LegacyKey });
        using var protector = Protector();
        await ClientImport.RunAsync(source, target, legacy, new(protector), Token);
        await Execute(target, "UPDATE public.clients SET first_name='Edited' WHERE first_name='First'");

        var result = await ClientImport.RunAsync(source, target, legacy, new(protector), Token);

        Assert.Equal("target_client_differs", Assert.Single(result.Issues).Code);
        Assert.Equal(2, result.ClientsMatched);
        Assert.Equal(0, result.ClientsInserted);
        Assert.Equal(1L, await Scalar<long>(target, "SELECT count(*) FROM public.clients WHERE first_name='Edited'"));
    });

    [Fact]
    public Task Run_CancelledBeforeSnapshot_DoesNotWriteTarget() => WithDatabases(async (source, target) =>
    {
        using var legacy = new LegacyPersonalDataReader(new Dictionary<string, string> { ["old"] = LegacyKey });
        using var protector = Protector();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ClientImport.RunAsync(source, target, legacy, new(protector), cancellation.Token));

        Assert.Equal(0L, await Scalar<long>(target, "SELECT count(*) FROM public.import_id_map"));
        Assert.Equal(0L, await Scalar<long>(target, "SELECT count(*) FROM public.clients"));
    });

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync(Token))!;
    }
}
