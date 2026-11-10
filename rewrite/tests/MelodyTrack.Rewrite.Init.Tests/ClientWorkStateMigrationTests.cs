using MelodyTrack.Rewrite.Init;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class ClientWorkStateMigrationTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Migration[] Migrations => MelodyTrack.Rewrite.Init.GeneratedMigrations.All.Where(step => step.Version <= 14).ToArray();
    private static Migration WorkStateMigration => Assert.Single(Migrations, step => step.Version == 14);
    private static readonly Guid ActiveClient = Guid.Parse("019a2c63-8c00-7000-8000-000000000001");
    private static readonly Guid ClosedClient = Guid.Parse("019a2c63-8c00-7000-8000-000000000002");
    private static readonly Guid ArchivedClient = Guid.Parse("019a2c63-8c00-7000-8000-000000000003");
    private static readonly Guid ClosedArchivedClient = Guid.Parse("019a2c63-8c00-7000-8000-000000000004");
    private static readonly Guid Staff = Guid.Parse("019a2c63-8c00-7000-8000-000000000005");
    private static readonly Guid Kind = Guid.Parse("019a2c63-8c00-7000-8000-000000000006");

    [Fact]
    public Task Apply_ExistingWorkStates_PreservesRecordsAndUnifiesArchiveWithoutRepeating() => WithPreviousSchema(async connection =>
    {
        await SeedClient(connection, ActiveClient, closed: false, archived: false);
        await SeedClient(connection, ClosedClient, closed: true, archived: false);
        await SeedClient(connection, ArchivedClient, closed: false, archived: true);
        await SeedClient(connection, ClosedArchivedClient, closed: true, archived: true);
        await Execute(connection, "INSERT INTO public.contact_kinds(id,name,format) VALUES ($1,'Synthetic phone','phone')", Kind);
        foreach (var client in new[] { ActiveClient, ClosedClient, ArchivedClient, ClosedArchivedClient })
        {
            var contact = Guid.NewGuid();
            await Execute(connection, """
                INSERT INTO public.client_contacts(id,client_id,kind_id,protected_value,protected_comment,is_primary)
                    VALUES ($1,$2,$3,'synthetic protected value','synthetic protected comment',true)
                """, contact, client, Kind);
            await Execute(connection,
                "INSERT INTO public.import_id_map(source_table,source_id,target_id) VALUES ('Clients',$1,$2)", client.ToByteArray(), client);
            await Execute(connection, """
                INSERT INTO public.import_id_map(source_table,source_id,part,target_id)
                    VALUES ('ClientContacts',$1,'synthetic owner:Phone',$2)
                """, contact.ToByteArray(), contact);
        }
        var preservedBefore = await PreservedRecords(connection);

        await Apply(connection, WorkStateMigration);

        Assert.Equal(preservedBefore, await PreservedRecords(connection));
        Assert.False(await Archived(connection, ActiveClient));
        Assert.True(await Archived(connection, ClosedClient));
        Assert.True(await Archived(connection, ArchivedClient));
        Assert.True(await Archived(connection, ClosedArchivedClient));
        Assert.False(await LegacyColumnExists(connection));
        Assert.Equal(14L, await Scalar<long>(connection, "SELECT count(*) FROM public.schema_migrations"));
        var stateAfterMigration = await FullClientState(connection);

        var pending = await MigrationHistory.GetPendingAsync(connection, Migrations, Token);
        foreach (var step in pending) await Apply(connection, step);

        Assert.Empty(pending);
        Assert.Equal(stateAfterMigration, await FullClientState(connection));
        Assert.Equal(preservedBefore, await PreservedRecords(connection));
        Assert.Equal(14L, await Scalar<long>(connection, "SELECT count(*) FROM public.schema_migrations"));
    });

    [Fact]
    public Task Apply_ClosedClientWithExistingAccess_RevokesOnlyItsCredentialsPermanently() => WithPreviousSchema(async connection =>
    {
        await SeedClient(connection, ClosedClient, closed: true, archived: false);
        await SeedClient(connection, ActiveClient, closed: false, archived: false);
        await Execute(connection, "INSERT INTO public.staff_access_state(staff_id,role) VALUES ($1,'administrator')", Staff);
        await SeedAccess(connection, ClosedClient, 'A', 'B', 'C');
        await SeedAccess(connection, ActiveClient, 'D', 'E', 'F');

        await Apply(connection, WorkStateMigration);

        Assert.Equal(1L, await Scalar<long>(connection,
            "SELECT count(*) FROM public.client_access_links WHERE client_id=$1 AND revoked_at IS NOT NULL", ClosedClient));
        Assert.Equal(1L, await Scalar<long>(connection, """
            SELECT count(*) FROM public.client_access_sessions s
            JOIN public.client_access_links l ON l.link_hash=s.link_hash
            WHERE l.client_id=$1 AND s.revoked_at IS NOT NULL
            """, ClosedClient));
        Assert.Equal(1L, await Scalar<long>(connection,
            "SELECT count(*) FROM public.client_access_links WHERE client_id=$1 AND revoked_at IS NULL", ActiveClient));
        Assert.Equal(1L, await Scalar<long>(connection, """
            SELECT count(*) FROM public.client_access_sessions s
            JOIN public.client_access_links l ON l.link_hash=s.link_hash
            WHERE l.client_id=$1 AND s.revoked_at IS NULL
            """, ActiveClient));
        var revokedState = await AccessState(connection);

        await Execute(connection, "UPDATE public.clients SET is_archived=false WHERE id=$1", ClosedClient);

        Assert.False(await Archived(connection, ClosedClient));
        Assert.Equal(revokedState, await AccessState(connection));
        Assert.Equal(2L, await Scalar<long>(connection, "SELECT count(*) FROM public.client_access_tokens"));
    });

    [Fact]
    public Task Apply_FailureAfterStateConversion_RollsBackSchemaRecordsAndAccess() => WithPreviousSchema(async connection =>
    {
        await SeedClient(connection, ClosedClient, closed: true, archived: false);
        await Execute(connection, "INSERT INTO public.staff_access_state(staff_id,role) VALUES ($1,'administrator')", Staff);
        await SeedAccess(connection, ClosedClient, 'A', 'B', 'C');
        var clientsBefore = await FullClientState(connection);
        var accessBefore = await AccessState(connection);
        var step = WorkStateMigration;
        var failing = step with
        {
            RunAsync = async context =>
            {
                await step.RunAsync(context);
                throw new InvalidOperationException("Synthetic failure after work-state migration.");
            }
        };

        var error = await Assert.ThrowsAsync<MigrationFailedException>(() => Apply(connection, failing));

        Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.True(await LegacyColumnExists(connection));
        Assert.Equal(clientsBefore, await FullClientState(connection));
        Assert.Equal(accessBefore, await AccessState(connection));
        Assert.Equal(13L, await Scalar<long>(connection, "SELECT count(*) FROM public.schema_migrations"));
        Assert.Equal(step, Assert.Single(await MigrationHistory.GetPendingAsync(connection, Migrations, Token)));

        await Apply(connection, step);

        Assert.False(await LegacyColumnExists(connection));
        Assert.True(await Archived(connection, ClosedClient));
        Assert.Equal(14L, await Scalar<long>(connection, "SELECT count(*) FROM public.schema_migrations"));
    });

    private async Task WithPreviousSchema(Func<NpgsqlConnection, Task> scenario)
    {
        var connectionString = await fixture.CreateDatabaseAsync(Token);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(Token);
        await MigrationRunner.PrepareAsync(connection, Token);
        foreach (var step in Migrations.Where(step => step.Version < 14)) await Apply(connection, step);
        await scenario(connection);
    }

    private static Task Apply(NpgsqlConnection connection, Migration migration) =>
        MigrationRunner.ApplyAsync(connection, migration, NullLogger.Instance, Token);

    private static Task SeedClient(NpgsqlConnection connection, Guid id, bool closed, bool archived) => Execute(connection, """
        INSERT INTO public.clients(id,first_name,last_name,middle_name,birthdate,protected_notes,created_at,is_lead_closed,is_archived)
        VALUES ($1,'Synthetic','Person','Preserved','2000-02-29','synthetic protected notes','2026-01-15T09:30:00Z',$2,$3)
        """, id, closed, archived);

    private static async Task SeedAccess(NpgsqlConnection connection, Guid client, char link, char refresh, char token)
    {
        var session = Guid.NewGuid();
        await Execute(connection, """
            INSERT INTO public.client_access_links(link_hash,client_id,issued_by,issued_at)
                VALUES ($1,$2,$3,'2026-01-15T10:00:00Z')
            """, new string(link, 64), client, Staff);
        await Execute(connection, """
            INSERT INTO public.client_access_sessions(id,link_hash,refresh_hash,created_at)
                VALUES ($1,$2,$3,'2026-01-15T10:00:00Z')
            """, session, new string(link, 64), new string(refresh, 64));
        await Execute(connection, """
            INSERT INTO public.client_access_tokens(token_hash,session_id,issued_at,expires_at)
                VALUES ($1,$2,'2026-01-15T10:00:00Z','2026-01-15T10:05:00Z')
            """, new string(token, 64), session);
    }

    private static Task<bool> Archived(NpgsqlConnection connection, Guid client) =>
        Scalar<bool>(connection, "SELECT is_archived FROM public.clients WHERE id=$1", client);

    private static Task<bool> LegacyColumnExists(NpgsqlConnection connection) => Scalar<bool>(connection, """
        SELECT EXISTS(SELECT 1 FROM information_schema.columns
            WHERE table_schema='public' AND table_name='clients' AND column_name='is_lead_closed')
        """);

    private static Task<string> PreservedRecords(NpgsqlConnection connection) => Scalar<string>(connection, """
        SELECT jsonb_build_object(
            'clients', (SELECT jsonb_agg(to_jsonb(c)-'is_lead_closed'-'is_archived' ORDER BY id) FROM public.clients c),
            'contacts', (SELECT jsonb_agg(to_jsonb(c) ORDER BY id) FROM public.client_contacts c),
            'id_maps', (SELECT jsonb_agg(to_jsonb(m) ORDER BY source_table,source_id,part) FROM public.import_id_map m))::text
        """);

    private static Task<string> FullClientState(NpgsqlConnection connection) => Scalar<string>(connection,
        "SELECT jsonb_agg(to_jsonb(c) ORDER BY id)::text FROM public.clients c");

    private static Task<string> AccessState(NpgsqlConnection connection) => Scalar<string>(connection, """
        SELECT jsonb_build_object(
            'links', (SELECT jsonb_agg(to_jsonb(l) ORDER BY link_hash) FROM public.client_access_links l),
            'sessions', (SELECT jsonb_agg(to_jsonb(s) ORDER BY id) FROM public.client_access_sessions s),
            'tokens', (SELECT jsonb_agg(to_jsonb(t) ORDER BY token_hash) FROM public.client_access_tokens t))::text
        """);

    private static async Task Execute(NpgsqlConnection connection, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task<T> Scalar<T>(NpgsqlConnection connection, string sql, params object[] values)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var value in values) command.Parameters.AddWithValue(value);
        return (T)(await command.ExecuteScalarAsync(Token))!;
    }
}
