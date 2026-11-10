using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Access;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class AuthBootstrapCommandTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly Guid Staff = Guid.Parse("019a2c63-8c00-7000-8000-000000000001");

    [Fact]
    public Task CreateAsync_InitialSetup_CreatesOneTimeSuperuserInvitationWithoutAccount() => WithDatabase(async database =>
    {
        var result = await AuthBootstrapCommand.CreateAsync(database, null, Token);

        Assert.True(result.IsSuccess);
        Assert.True(OpaqueTokens.IsValid(result.Value));
        var link = await database.Query("""
            SELECT role::text AS "Role", staff_id AS "StaffId", consumed AS "Consumed",
                EXTRACT(EPOCH FROM (expires_at-clock_timestamp())) AS "Seconds"
            FROM public.staff_credential_links WHERE token_hash=@hash
            """).WithParameter("hash", OpaqueTokens.Hash(result.Value)).GetSingleAsync<LinkRow>(Token);
        Assert.Equal("superuser", link.Role);
        Assert.Null(link.StaffId);
        Assert.False(link.Consumed);
        Assert.InRange(link.Seconds, 86340m, 86400m);
        Assert.Equal(0, await database.Query("SELECT count(*)::integer FROM public.staff_access_state").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task CreateAsync_ExplicitLostSuperuser_RevokesSessionsAndRemovesLostCredentials() => WithDatabase(async database =>
    {
        await SeedAsync(database, "superuser");
        var access = OpaqueTokens.Create();
        var session = Guid.NewGuid();
        var tokenStore = new StaffAccessTokenStore(database, new TrustedInternalBrowserAccessContext());
        Assert.True((await tokenStore.AddAsync(OpaqueTokens.Hash(access), new(Staff), session, Token)).IsSuccess);
        var oldLink = OpaqueTokens.Hash(OpaqueTokens.Create());
        await database.Query("""
            INSERT INTO public.staff_passkeys(credential_id,staff_id,public_key,signature_counter,user_handle)
            VALUES ('synthetic-credential',@staff,decode('010203','hex'),0,decode('010203','hex'));
            INSERT INTO public.staff_credential_links(id,token_hash,role,staff_id,expires_at,consumed)
            VALUES (@id,@hash,'superuser',@staff,clock_timestamp()+INTERVAL '24 hours',false);
            """).WithParameter("staff", Staff).WithParameter("id", Guid.NewGuid()).WithParameter("hash", oldLink).ExecuteAsync(Token);

        var result = await AuthBootstrapCommand.CreateAsync(database, Staff, Token);

        Assert.True(result.IsSuccess);
        Assert.Null(await tokenStore.FindStaffAsync(OpaqueTokens.Hash(access), Token));
        Assert.True(await database.Query("SELECT password_hash IS NULL AND protected_authenticator IS NULL AND second_factor_enabled FROM public.staff_credentials WHERE staff_id=@id")
            .WithParameter("id", Staff).GetSingleAsync<bool>(Token));
        Assert.Equal(0, await database.Query("SELECT count(*)::integer FROM public.staff_passkeys").GetSingleAsync<int>(Token));
        Assert.True(await database.Query("SELECT consumed FROM public.staff_credential_links WHERE token_hash=@hash")
            .WithParameter("hash", oldLink).GetSingleAsync<bool>(Token));
        var newLink = await database.Query("SELECT staff_id AS \"StaffId\", role::text AS \"Role\", consumed AS \"Consumed\" FROM public.staff_credential_links WHERE token_hash=@hash")
            .WithParameter("hash", OpaqueTokens.Hash(result.Value)).GetSingleAsync<LinkRow>(Token);
        Assert.Equal(Staff, newLink.StaffId);
        Assert.Equal("superuser", newLink.Role);
        Assert.False(newLink.Consumed);
    });

    [Fact]
    public Task CreateAsync_OrdinaryStaffTarget_DoesNotAlterCredentials() => WithDatabase(async database =>
    {
        await SeedAsync(database, "teacher");

        var result = await AuthBootstrapCommand.CreateAsync(database, Staff, Token);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error is ModelError.NotFound);
        Assert.True(await database.Query("SELECT password_hash IS NOT NULL FROM public.staff_credentials WHERE staff_id=@id")
            .WithParameter("id", Staff).GetSingleAsync<bool>(Token));
        Assert.Equal(0, await database.Query("SELECT count(*)::integer FROM public.staff_credential_links").GetSingleAsync<int>(Token));
    });

    private static Task SeedAsync(Database database, string role) => database.Query("""
        INSERT INTO public.staff_access_state(staff_id,role) VALUES (@staff,CAST(@role AS public.staff_access_role));
        INSERT INTO public.staff_credentials(staff_id,login,normalized_login,password_hash,protected_authenticator,second_factor_enabled)
        VALUES (@staff,'synthetic','SYNTHETIC','synthetic-password-hash','synthetic-protected-secret',true)
        """).WithParameter("staff", Staff).WithParameter("role", role).ExecuteAsync(Token);

    private async Task WithDatabase(Func<Database, Task> action)
    {
        await using var dataSource = NpgsqlDataSource.Create(await fixture.CreateDatabaseAsync(Token));
        var database = new Database(dataSource);
        foreach (var migration in new[] { "0001_CreateClients.sql", "0003_CreateClientNotesHistory.sql", "0008_CreateStaffPermissions.sql",
            "0010_CreateStaffAccessTokens.sql", "0011_CreateStaffCredentials.sql", "0012_CreateClientAccessSessions.sql", "0013_CreateBrowserAccessContexts.sql" })
            await database.Query(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", migration), Token)).ExecuteAsync(Token);
        await action(database);
    }

    private sealed class LinkRow
    {
        public string Role { get; init; } = "";
        public Guid? StaffId { get; init; }
        public bool Consumed { get; init; }
        public decimal Seconds { get; init; }
    }
}
