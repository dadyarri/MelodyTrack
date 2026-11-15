using System.Security.Cryptography;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Access;
using MelodyTrack.Rewrite.Persistence.Clients;
using MelodyTrack.Rewrite.Persistence.Security;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class ClientNotesStoreTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly ClientId Client = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000001"));
    private static readonly StaffId Author = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000002"));
    private static readonly StaffId Super = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000003"));
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task ChangeAsync_RoleRevocationCommitsBeforeAuthorityCheck_DeniesWithoutWriting() => WithStore(async (store, database, _) =>
    {
        await using var revocation = await database.BeginTransactionAsync(Token);
        await revocation.Query("UPDATE public.role_permission_policy SET allowed=false WHERE role='administrator' AND permission='client_notes_write'").ExecuteAsync(Token);
        var revokerPid = await revocation.Query("SELECT pg_backend_pid()").GetSingleAsync<int>(Token);

        var pending = store.ChangeAsync(Command("initial", "next"), Token);
        await WaitForBlockedAsync(database, revokerPid);
        await revocation.CommitAsync(Token);
        var result = await pending;

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error is ModelError.Forbidden);
        Assert.Equal("initial", Ok(await store.GetCurrentAsync(Client, Token)));
        Assert.Empty(Ok(await store.GetHistoryAsync(Client, Token)));
    });

    [Theory]
    [InlineData("role_policy")]
    [InlineData("personal_policy")]
    [InlineData("blocked")]
    [InlineData("archived")]
    [InlineData("role_change")]
    public Task ChangeAsync_AuthorizedWriteInProgress_SerializesRevocationAfterCommit(string revocationKind) => WithStore(async (store, database, _) =>
    {
        await using var clientLock = await database.BeginTransactionAsync(Token);
        await clientLock.Query("SELECT id FROM public.clients WHERE id=@id FOR UPDATE").WithParameter("id", Client.Value).GetSingleAsync<Guid>(Token);
        var clientLockerPid = await clientLock.Query("SELECT pg_backend_pid()").GetSingleAsync<int>(Token);
        var pendingWrite = store.ChangeAsync(Command("initial", "next"), Token);
        var writerPid = await WaitForBlockedAsync(database, clientLockerPid);
        var policies = new StaffPolicyStore(database);

        Task revoke = revocationKind switch
        {
            "role_policy" => policies.SetRolePolicyAsync(Guid.NewGuid(), Super, StaffRole.Administrator,
                StaffPermission.ClientNotesWrite, PermissionGrant.Denied, Token),
            "personal_policy" => policies.SetPersonalPolicyAsync(Guid.NewGuid(), Super, Author,
                StaffPermission.ClientNotesWrite, PermissionGrant.Denied, Token),
            "blocked" => database.Query("UPDATE public.staff_access_state SET is_blocked=true WHERE staff_id=@id").WithParameter("id", Author.Value).ExecuteAsync(Token),
            "archived" => database.Query("UPDATE public.staff_access_state SET is_archived=true WHERE staff_id=@id").WithParameter("id", Author.Value).ExecuteAsync(Token),
            _ => database.Query("UPDATE public.staff_access_state SET role='teacher' WHERE staff_id=@id").WithParameter("id", Author.Value).ExecuteAsync(Token)
        };
        await WaitForBlockedAsync(database, writerPid);
        await clientLock.CommitAsync(Token);
        var written = Ok(await pendingWrite);
        await revoke;
        var denied = await store.ChangeAsync(Command("next", "forbidden"), Token);

        Assert.Equal("next", written.After);
        Assert.True(denied.IsFailure);
        Assert.Contains(denied.Error.Issues, issue => issue.Error is ModelError.Forbidden);
        var history = Assert.Single(Ok(await store.GetHistoryAsync(Client, Token)));
        AssertHistoryEntry(written, history);
        Assert.Equal(written, Ok(await store.FindChangeAsync(written.Id, Token)));
    });

    [Fact]
    public Task ChangeAsync_ReplayAfterRevocation_DeniesBeforeDecryptingHistory() => WithStore(async (store, database, _) =>
    {
        var command = Command("initial", "next");
        var original = Ok(await store.ChangeAsync(command, Token));
        await database.Query("UPDATE public.staff_access_state SET is_blocked=true WHERE staff_id=@id").WithParameter("id", Author.Value).ExecuteAsync(Token);
        await database.Query("UPDATE public.client_notes_history SET protected_after='corrupt' WHERE id=@id").WithParameter("id", command.EntryId).ExecuteAsync(Token);

        var replay = await store.ChangeAsync(command, Token);
        var rawReplay = await store.AppendAsync(original, Token);

        Assert.True(replay.IsFailure);
        Assert.True(rawReplay.IsFailure);
        Assert.Contains(replay.Error.Issues, issue => issue.Error is ModelError.Forbidden);
        Assert.Contains(rawReplay.Error.Issues, issue => issue.Error is ModelError.Forbidden);
    });

    [Fact]
    public Task ChangeAsync_OwnScopeWithoutVerifiedClientOwner_DeniesWrite() => WithStore(async (store, database, _) =>
    {
        var policies = new StaffPolicyStore(database);
        var granted = await policies.SetPersonalPolicyAsync(Guid.NewGuid(), Super, Author, StaffPermission.ClientNotesWrite,
            new(true, PermissionDataScope.Own, PermissionTimeScope.Any), Token);
        Assert.True(granted.IsSuccess);

        var result = await store.ChangeAsync(Command("initial", "next"), Token);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error is ModelError.Forbidden);
        Assert.Equal("initial", Ok(await store.GetCurrentAsync(Client, Token)));
    });

    [Fact]
    public Task ChangeAsync_TodayScopeWithOlderSelectedTime_DeniesWrite() => WithStore(async (store, database, _) =>
    {
        var policies = new StaffPolicyStore(database);
        Assert.True((await policies.SetPersonalPolicyAsync(Guid.NewGuid(), Super, Author, StaffPermission.ClientNotesWrite,
            new(true, PermissionDataScope.Available, PermissionTimeScope.Today), Token)).IsSuccess);
        var serverNow = await database.Query("SELECT CURRENT_TIMESTAMP").GetSingleAsync<DateTime>(Token);
        var command = Command("initial", "next") with { ChangedAt = new DateTimeOffset(serverNow, TimeSpan.Zero).AddDays(-2) };

        var result = await store.ChangeAsync(command, Token);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error is ModelError.Forbidden);
        Assert.Empty(Ok(await store.GetHistoryAsync(Client, Token)));
    });


    [Fact]
    public Task AppendAsync_ChangedNotes_ProtectsCurrentAndHistoryAndReplaysSafely() => WithStore(async (store, database, _) =>
    {
        var first = Change("initial", "  next  ");

        var saved = Ok(await store.AppendAsync(first, Token));
        var second = Change("  next  ", null);
        Ok(await store.AppendAsync(second, Token));
        var replayed = Ok(await store.AppendAsync(first, Token));

        Assert.Equal(saved, replayed);
        Assert.Null(Ok(await store.GetCurrentAsync(Client, Token)));
        var history = Ok(await store.GetHistoryAsync(Client, Token));
        Assert.Equal(2, history.Count);
        AssertHistoryEntry(first, Assert.Single(history, entry => entry.Id == first.Id));
        AssertHistoryEntry(second, Assert.Single(history, entry => entry.Id == second.Id));
        Assert.Equal(first, Ok(await store.FindChangeAsync(first.Id, Token)));
        var protectedBefore = await database.Query("SELECT protected_before FROM public.client_notes_history WHERE id = @id")
            .WithParameter("id", first.Id).GetSingleAsync<string>(Token);
        var protectedAfter = await database.Query("SELECT protected_after FROM public.client_notes_history WHERE id = @id")
            .WithParameter("id", first.Id).GetSingleAsync<string>(Token);
        Assert.StartsWith("mtpii:1:", protectedBefore);
        Assert.StartsWith("mtpii:1:", protectedAfter);
        Assert.NotEqual(first.Before, protectedBefore);
        Assert.NotEqual(first.After, protectedAfter);
    });

    [Fact]
    public Task AppendAsync_StaleOriginalText_RejectsWithoutPartialHistory() => WithStore(async (store, _, _) =>
    {
        var first = Change("initial", "first");
        var stale = Change("initial", "stale");
        Ok(await store.AppendAsync(first, Token));

        var result = await store.AppendAsync(stale, Token);

        Assert.True(result.IsFailure);
        Assert.Equal("first", Ok(await store.GetCurrentAsync(Client, Token)));
        AssertHistoryEntry(first, Assert.Single(Ok(await store.GetHistoryAsync(Client, Token))));
    });

    [Fact]
    public Task AppendAsync_HistoryInsertFails_RollsBackCurrentText() => WithStore(async (store, database, _) =>
    {
        await database.Query("""
            CREATE FUNCTION public.reject_notes_history() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Rejected history'; END; $$;
            CREATE TRIGGER reject_notes_history BEFORE INSERT ON public.client_notes_history
            FOR EACH ROW EXECUTE FUNCTION public.reject_notes_history();
            """).ExecuteAsync(Token);

        await Assert.ThrowsAsync<PostgresException>(() => store.AppendAsync(Change("initial", "next"), Token));

        Assert.Equal("initial", Ok(await store.GetCurrentAsync(Client, Token)));
        Assert.Empty(Ok(await store.GetHistoryAsync(Client, Token)));
    });

    [Fact]
    public Task AppendAsync_ConcurrentChangesFromSameOriginal_AllowsOneCompleteChange() => WithStore(async (store, _, _) =>
    {
        var changes = new[] { Change("initial", "first"), Change("initial", "second") };

        var results = await Task.WhenAll(changes.Select(change => store.AppendAsync(change, Token)));

        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => result.IsFailure);
        var history = Assert.Single(Ok(await store.GetHistoryAsync(Client, Token)));
        Assert.Equal(history.After, Ok(await store.GetCurrentAsync(Client, Token)));
        Assert.Equal("initial", history.Before);
    });

    [Fact]
    public Task AppendAsync_ConcurrentReplay_RecordsSingleHistoryEntry() => WithStore(async (store, _, _) =>
    {
        var change = Change("initial", "first");

        var results = await Task.WhenAll(store.AppendAsync(change, Token), store.AppendAsync(change, Token));

        Assert.All(results, result => Assert.Equal(change, Ok(result)));
        AssertHistoryEntry(change, Assert.Single(Ok(await store.GetHistoryAsync(Client, Token))));
    });

    [Fact]
    public Task AppendAsync_MissingClient_RejectsWithoutHistory() => WithStore(async (store, _, _) =>
    {
        var change = Ok(ClientNotesChange.Create(Guid.NewGuid(), new ClientId(Guid.NewGuid()), Author, Now, null, "next"));

        var result = await store.AppendAsync(change, Token);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error is ModelError.NotFound);
        Assert.Empty(Ok(await store.GetHistoryAsync(Client, Token)));
    });

    private async Task WithStore(Func<ClientNotesStore, Database, ClientDataProtection, Task> scenario)
    {
        await using var dataSource = NpgsqlDataSource.Create(await fixture.CreateDatabaseAsync(Token));
        var database = new Database(dataSource);
        foreach (var migration in new[] { "0001_CreateClients.sql", "0003_CreateClientNotesHistory.sql", "0008_CreateStaffPermissions.sql", "0010_CreateStaffAccessTokens.sql", "0011_CreateStaffCredentials.sql" })
        {
            var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", migration), Token);
            await database.Query(sql).ExecuteAsync(Token);
        }
        using var protector = new PersonalDataProtector("test", new Dictionary<string, string>
        {
            ["test"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        });
        var protection = new ClientDataProtection(protector);
        await database.Query("""
            INSERT INTO public.staff_access_state(staff_id, role) VALUES (@author, 'administrator'), (@super, 'superuser')
            """).WithParameter("author", Author.Value).WithParameter("super", Super.Value).ExecuteAsync(Token);
        await database.Query("""
            INSERT INTO public.clients(id, first_name, last_name, protected_notes, created_at)
            VALUES (@id, 'Synthetic', 'Client', @notes, @createdAt)
            """).WithParameter("id", Client.Value).WithParameter("notes", protection.ProtectNotes(Client, "initial"))
            .WithParameter("createdAt", Now).ExecuteAsync(Token);
        await scenario(new ClientNotesStore(database, protection), database, protection);
    }

    private static ClientNotesChange Change(string? before, string? after) =>
        Ok(ClientNotesChange.Create(Guid.NewGuid(), Client, Author, Now, before, after));

    private static ClientNotesWriteCommand Command(string? before, string? after) =>
        new(Guid.NewGuid(), Client, Author, Now, after, before, true);

    private static void AssertHistoryEntry(ClientNotesChange expected, ClientNotesHistoryEntry actual)
    {
        Assert.Equal(expected.Id, actual.Id);
        Assert.Equal("Сотрудник", actual.AuthorName);
        Assert.Equal(expected.ChangedAt, actual.ChangedAt);
        Assert.Equal(expected.Before, actual.Before);
        Assert.Equal(expected.After, actual.After);
    }

    private static async Task<int> WaitForBlockedAsync(Database database, int blockingPid)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        while (true)
        {
            var waitingPid = await database.Query("""
                SELECT pid FROM pg_stat_activity
                WHERE datname=current_database() AND @blockingPid=ANY(pg_blocking_pids(pid)) ORDER BY pid LIMIT 1
                """).WithParameter("blockingPid", blockingPid).GetSingleOrDefaultAsync<int?>(timeout.Token);
            if (waitingPid is { } pid) return pid;
            await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
        }
    }

    private static T Ok<T>(Result<T, ErrorList> result)
    {
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
