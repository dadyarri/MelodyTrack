using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Access;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class StaffPolicyStoreTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly StaffId Super = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000001"));
    private static readonly StaffId First = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000002"));
    private static readonly StaffId Second = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000003"));
    private static PermissionGrant Allow => new(true, PermissionDataScope.Available, PermissionTimeScope.Any);

    [Fact]
    public Task ResolveAsync_InitialTeacher_DeniesNotesWriteAndAllowsNotesRead() => WithStore(async (store, _) =>
    {
        var actor = Ok(await store.ResolveAsync(First, Token));

        Assert.True(actor.HasPermission(StaffPermission.ClientNotesRead));
        Assert.False(actor.HasPermission(StaffPermission.ClientNotesWrite));
    });

    [Fact]
    public Task SetRolePolicyAsync_ExistingPersonalException_InheritsOnlyUnchangedActions() => WithStore(async (store, _) =>
    {
        Assert.True((await store.SetPersonalPolicyAsync(Guid.NewGuid(), Super, First, StaffPermission.ClientNotesWrite, PermissionGrant.Denied, Token)).IsSuccess);

        Assert.True((await store.SetRolePolicyAsync(Guid.NewGuid(), Super, StaffRole.Teacher, StaffPermission.ClientNotesWrite, Allow, Token)).IsSuccess);

        Assert.False(Ok(await store.ResolveAsync(First, Token)).HasPermission(StaffPermission.ClientNotesWrite));
        Assert.True(Ok(await store.ResolveAsync(Second, Token)).HasPermission(StaffPermission.ClientNotesWrite));
        Assert.True((await store.SetPersonalPolicyAsync(Guid.NewGuid(), Super, First, StaffPermission.ClientNotesWrite, null, Token)).IsSuccess);
        Assert.True(Ok(await store.ResolveAsync(First, Token)).HasPermission(StaffPermission.ClientNotesWrite));
    });

    [Fact]
    public Task SetPersonalPolicyAsync_ReplayAfterNewerChange_DoesNotRestoreOldGrant() => WithStore(async (store, database) =>
    {
        var commandId = Guid.NewGuid();
        Assert.True((await store.SetPersonalPolicyAsync(commandId, Super, First, StaffPermission.ClientNotesWrite, Allow, Token)).IsSuccess);
        Assert.True((await store.SetPersonalPolicyAsync(Guid.NewGuid(), Super, First, StaffPermission.ClientNotesWrite, PermissionGrant.Denied, Token)).IsSuccess);

        var replay = await store.SetPersonalPolicyAsync(commandId, Super, First, StaffPermission.ClientNotesWrite, Allow, Token);

        Assert.True(replay.IsSuccess);
        Assert.False(Ok(await store.ResolveAsync(First, Token)).HasPermission(StaffPermission.ClientNotesWrite));
        Assert.Equal(2, await database.Query("SELECT count(*)::integer FROM public.staff_policy_commands").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task ResolveAsync_UserBlockedAfterGrant_DeniesNextRequest() => WithStore(async (store, database) =>
    {
        Assert.True((await store.SetPersonalPolicyAsync(Guid.NewGuid(), Super, First, StaffPermission.ClientNotesWrite, Allow, Token)).IsSuccess);
        Assert.True(Ok(await store.ResolveAsync(First, Token)).HasPermission(StaffPermission.ClientNotesWrite));
        await database.Query("UPDATE public.staff_access_state SET is_blocked=true WHERE staff_id=@id").WithParameter("id", First.Value).ExecuteAsync(Token);

        var result = await store.ResolveAsync(First, Token);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error is ModelError.Forbidden);
    });

    [Fact]
    public Task SetRolePolicyAsync_OrdinaryStaffOrSuperuserTarget_DeniesChange() => WithStore(async (store, _) =>
    {
        var ordinaryAuthor = await store.SetRolePolicyAsync(Guid.NewGuid(), First, StaffRole.Teacher, StaffPermission.ClientNotesWrite, Allow, Token);
        var immutableRole = await store.SetRolePolicyAsync(Guid.NewGuid(), Super, StaffRole.Superuser, StaffPermission.ClientNotesWrite, PermissionGrant.Denied, Token);
        var delegatePolicyEditing = await store.SetPersonalPolicyAsync(Guid.NewGuid(), Super, First, StaffPermission.PoliciesManage, Allow, Token);

        Assert.True(ordinaryAuthor.IsFailure);
        Assert.True(immutableRole.IsFailure);
        Assert.True(delegatePolicyEditing.IsFailure);
        Assert.False(Ok(await store.ResolveAsync(First, Token)).HasPermission(StaffPermission.ClientNotesWrite));
    });

    [Fact]
    public Task SetPersonalPolicyAsync_ConcurrentIdenticalCommands_RecordsOneReceipt() => WithStore(async (store, database) =>
    {
        var id = Guid.NewGuid();

        var results = await Task.WhenAll(store.SetPersonalPolicyAsync(id, Super, First, StaffPermission.ClientNotesWrite, Allow, Token),
            store.SetPersonalPolicyAsync(id, Super, First, StaffPermission.ClientNotesWrite, Allow, Token));

        Assert.All(results, result => Assert.True(result.IsSuccess));
        Assert.Equal(1, await database.Query("SELECT count(*)::integer FROM public.staff_policy_commands").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task SetPersonalPolicyAsync_SuperuserTarget_DeniesChangeWithoutHistory() => WithStore(async (store, database) =>
    {
        var result = await store.SetPersonalPolicyAsync(Guid.NewGuid(), Super, Super,
            StaffPermission.ClientNotesWrite, PermissionGrant.Denied, Token);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error is ModelError.Forbidden);
        Assert.Equal(0, await database.Query("SELECT count(*)::integer FROM public.staff_policy_commands").GetSingleAsync<int>(Token));
        Assert.True(Ok(await store.ResolveAsync(Super, Token)).HasPermission(StaffPermission.ClientNotesWrite));
    });

    [Fact]
    public Task SetRolePolicyAsync_AuthorDemotedAfterRequestSnapshot_DeniesChange() => WithStore(async (store, database) =>
    {
        var author = Ok(await store.ResolveAsync(Super, Token));
        await database.Query("UPDATE public.staff_access_state SET role='administrator' WHERE staff_id=@id")
            .WithParameter("id", Super.Value).ExecuteAsync(Token);

        var result = await new StaffPolicies(store).SetRoleAsync(Guid.NewGuid(), author, StaffRole.Teacher,
            StaffPermission.ClientNotesWrite, Allow, Token);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Error is ModelError.Forbidden);
        Assert.False(Ok(await store.ResolveAsync(First, Token)).HasPermission(StaffPermission.ClientNotesWrite));
        Assert.Equal(0, await database.Query("SELECT count(*)::integer FROM public.staff_policy_commands").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task SetRolePolicyAsync_ReplayAfterNewerChange_PreservesCurrentPolicy() => WithStore(async (store, database) =>
    {
        var command = Guid.NewGuid();
        Assert.True((await store.SetRolePolicyAsync(command, Super, StaffRole.Teacher, StaffPermission.ClientNotesWrite, Allow, Token)).IsSuccess);
        Assert.True((await store.SetRolePolicyAsync(Guid.NewGuid(), Super, StaffRole.Teacher, StaffPermission.ClientNotesWrite, PermissionGrant.Denied, Token)).IsSuccess);

        var replay = await store.SetRolePolicyAsync(command, Super, StaffRole.Teacher, StaffPermission.ClientNotesWrite, Allow, Token);
        var changedMeaning = await store.SetRolePolicyAsync(command, Super, StaffRole.Teacher, StaffPermission.ClientNotesWrite, PermissionGrant.Denied, Token);

        Assert.True(replay.IsSuccess);
        Assert.True(changedMeaning.IsFailure);
        Assert.False(Ok(await store.ResolveAsync(First, Token)).HasPermission(StaffPermission.ClientNotesWrite));
        Assert.Equal(2, await database.Query("SELECT count(*)::integer FROM public.staff_policy_commands").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task SetPersonalPolicyAsync_HistoryWriteFails_DoesNotChangePermission() => WithStore(async (store, database) =>
    {
        await database.Query("""
            CREATE FUNCTION public.reject_policy_command() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Rejected command'; END; $$;
            CREATE TRIGGER reject_policy_command BEFORE INSERT ON public.staff_policy_commands
            FOR EACH ROW EXECUTE FUNCTION public.reject_policy_command();
            """).ExecuteAsync(Token);

        await Assert.ThrowsAsync<PostgresException>(() => store.SetPersonalPolicyAsync(Guid.NewGuid(), Super, First, StaffPermission.ClientNotesWrite, Allow, Token));

        Assert.False(Ok(await store.ResolveAsync(First, Token)).HasPermission(StaffPermission.ClientNotesWrite));
    });

    private async Task WithStore(Func<StaffPolicyStore, Database, Task> scenario)
    {
        await using var dataSource = NpgsqlDataSource.Create(await fixture.CreateDatabaseAsync(Token));
        var database = new Database(dataSource);
        foreach (var migration in new[] { "0001_CreateClients.sql", "0003_CreateClientNotesHistory.sql", "0008_CreateStaffPermissions.sql" })
            await database.Query(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", migration), Token)).ExecuteAsync(Token);
        await database.Query("""
            INSERT INTO public.staff_access_state(staff_id, role)
            VALUES (@super, 'superuser'), (@first, 'teacher'), (@second, 'teacher')
            """).WithParameter("super", Super.Value).WithParameter("first", First.Value).WithParameter("second", Second.Value).ExecuteAsync(Token);
        await scenario(new StaffPolicyStore(database), database);
    }

    private static T Ok<T>(Result<T, ErrorList> result)
    {
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
