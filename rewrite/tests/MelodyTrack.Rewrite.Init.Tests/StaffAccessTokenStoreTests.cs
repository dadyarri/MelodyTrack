using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Access;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class StaffAccessTokenStoreTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly StaffId First = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000001"));
    private static readonly StaffId Second = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000002"));
    private static string Hash(char value) => new(value, 64);

    [Fact]
    public Task FindStaffAsync_ValidToken_ReturnsSessionIdentityAndFiveMinuteLifetime() => WithStore(async (store, database) =>
    {
        var session = Guid.NewGuid();
        Ok(await store.AddAsync(Hash('A'), First, session, Token));

        var identity = await store.FindStaffAsync(Hash('A'), Token);

        Assert.Equal(new StaffSessionIdentity(First, session), identity);
        Assert.Equal(300m, await database.Query("SELECT EXTRACT(EPOCH FROM (expires_at-issued_at)) FROM public.staff_access_tokens")
            .GetSingleAsync<decimal>(Token));
        Assert.Null(await store.FindStaffAsync(Hash('B'), Token));
    });

    [Fact]
    public Task FindStaffAsync_ExpiredToken_DeniesWithoutWaiting() => WithStore(async (store, database) =>
    {
        Ok(await store.AddAsync(Hash('A'), First, Guid.NewGuid(), Token));
        await database.Query("""
            UPDATE public.staff_access_tokens
            SET issued_at=CURRENT_TIMESTAMP - INTERVAL '6 minutes', expires_at=CURRENT_TIMESTAMP - INTERVAL '1 minute'
            """).ExecuteAsync(Token);

        var identity = await store.FindStaffAsync(Hash('A'), Token);

        Assert.Null(identity);
    });

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public Task FindStaffAsync_AccountNoLongerActive_DeniesExistingTokenAndNewIssue(bool blocked, bool archived) => WithStore(async (store, database) =>
    {
        var session = Guid.NewGuid();
        Ok(await store.AddAsync(Hash('A'), First, session, Token));
        await database.Query("UPDATE public.staff_access_state SET is_blocked=@blocked, is_archived=@archived WHERE staff_id=@staff")
            .WithParameter("blocked", blocked).WithParameter("archived", archived).WithParameter("staff", First.Value).ExecuteAsync(Token);

        var identity = await store.FindStaffAsync(Hash('A'), Token);
        var issue = await store.AddAsync(Hash('B'), First, session, Token);

        Assert.Null(identity);
        Assert.True(issue.IsFailure);
        Assert.Equal(1, await database.Query("SELECT count(*)::integer FROM public.staff_access_tokens").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task RevokeSessionAsync_MultipleTokens_DeniesAllAndPreventsReissue() => WithStore(async (store, database) =>
    {
        var session = Guid.NewGuid();
        Ok(await store.AddAsync(Hash('A'), First, session, Token));
        Ok(await store.AddAsync(Hash('B'), First, session, Token));
        var otherSession = Guid.NewGuid();
        Ok(await store.AddAsync(Hash('C'), First, otherSession, Token));

        await store.RevokeSessionAsync(session, Token);
        await store.RevokeSessionAsync(session, Token);
        var reissue = await store.AddAsync(Hash('D'), First, session, Token);

        Assert.Null(await store.FindStaffAsync(Hash('A'), Token));
        Assert.Null(await store.FindStaffAsync(Hash('B'), Token));
        Assert.Equal(new StaffSessionIdentity(First, otherSession), await store.FindStaffAsync(Hash('C'), Token));
        Assert.True(reissue.IsFailure);
        Assert.Equal(3, await database.Query("SELECT count(*)::integer FROM public.staff_access_tokens").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task AddAsync_ExistingSessionOwnedByAnotherStaff_DeniesRebinding() => WithStore(async (store, database) =>
    {
        var session = Guid.NewGuid();
        Ok(await store.AddAsync(Hash('A'), First, session, Token));

        var result = await store.AddAsync(Hash('B'), Second, session, Token);

        Assert.True(result.IsFailure);
        Assert.Equal(new StaffSessionIdentity(First, session), await store.FindStaffAsync(Hash('A'), Token));
        Assert.Null(await store.FindStaffAsync(Hash('B'), Token));
        Assert.Equal(First.Value, await database.Query("SELECT staff_id FROM public.staff_sessions WHERE id=@session")
            .WithParameter("session", session).GetSingleAsync<Guid>(Token));
    });

    [Fact]
    public Task AddAsync_DuplicateHash_DoesNotRebindOrCreateAnotherSession() => WithStore(async (store, database) =>
    {
        var session = Guid.NewGuid();
        Ok(await store.AddAsync(Hash('A'), First, session, Token));

        var result = await store.AddAsync(Hash('A'), Second, Guid.NewGuid(), Token);

        Assert.True(result.IsFailure);
        Assert.Equal(new StaffSessionIdentity(First, session), await store.FindStaffAsync(Hash('A'), Token));
        Assert.Equal(1, await database.Query("SELECT count(*)::integer FROM public.staff_sessions").GetSingleAsync<int>(Token));
    });

    [Theory]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public Task AddAsync_InvalidHash_DoesNotPersistSession(string hash) => WithStore(async (store, database) =>
    {
        var result = await store.AddAsync(hash, First, Guid.NewGuid(), Token);

        Assert.True(result.IsFailure);
        Assert.Null(await store.FindStaffAsync(hash, Token));
        Assert.Equal(0, await database.Query("SELECT count(*)::integer FROM public.staff_sessions").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task RevokeSessionAsync_ConcurrentIssue_CannotLeaveNewTokenUsable() => WithStore(async (store, _) =>
    {
        var session = Guid.NewGuid();
        Ok(await store.AddAsync(Hash('A'), First, session, Token));

        await Task.WhenAll(store.AddAsync(Hash('B'), First, session, Token), store.RevokeSessionAsync(session, Token));

        Assert.Null(await store.FindStaffAsync(Hash('A'), Token));
        Assert.Null(await store.FindStaffAsync(Hash('B'), Token));
        Assert.True((await store.AddAsync(Hash('C'), First, session, Token)).IsFailure);
    });

    private async Task WithStore(Func<StaffAccessTokenStore, Database, Task> scenario)
    {
        await using var dataSource = NpgsqlDataSource.Create(await fixture.CreateDatabaseAsync(Token));
        var database = new Database(dataSource);
        foreach (var migration in new[] { "0001_CreateClients.sql", "0003_CreateClientNotesHistory.sql",
            "0008_CreateStaffPermissions.sql", "0010_CreateStaffAccessTokens.sql", "0011_CreateStaffCredentials.sql",
            "0012_CreateClientAccessSessions.sql", "0013_CreateBrowserAccessContexts.sql" })
            await database.Query(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", migration), Token)).ExecuteAsync(Token);
        await database.Query("INSERT INTO public.staff_access_state(staff_id, role) VALUES (@first, 'administrator'), (@second, 'teacher')")
            .WithParameter("first", First.Value).WithParameter("second", Second.Value).ExecuteAsync(Token);
        await scenario(new StaffAccessTokenStore(database, new TrustedInternalBrowserAccessContext()), database);
    }

    private static void Ok(UnitResult<ErrorList> result) => Assert.True(result.IsSuccess);
}
