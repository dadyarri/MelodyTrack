using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Finance;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class ExpenseStoreTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Actor Super => new Actor.Staff(new(Guid.Parse("019a2c63-8c00-7000-8000-000000000001")), StaffRole.Superuser);
    private static DateTimeOffset Now => new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public Task ApplyAsync_CreateEditDeleteAndReplay_PreservesHistoryAndKopecks() => WithStore(async (store, database) =>
    {
        var created = Create();
        var original = created.After!;
        var commandId = Guid.NewGuid();
        Ok(await store.ApplyAsync(commandId, created, Token));
        var edited = Ok(original.Edit("Материалы", Ok(Money.Create(199.99m)), original.Date,
            null, "Исправление", Super, Now.AddMinutes(1)));
        Ok(await store.ApplyAsync(Guid.NewGuid(), edited, Token));

        var replayed = Ok(await store.ApplyAsync(commandId, created with { OccurredAt = Now.AddHours(1) }, Token));

        Assert.Equal(created.OccurredAt, replayed.OccurredAt);
        Assert.Equal(edited.After, Ok(await store.FindAsync(original.Id, Token)));
        Ok(await store.ApplyAsync(Guid.NewGuid(), Ok(edited.After!.Delete(null, Super, Now.AddMinutes(2))), Token));
        Assert.Null(Ok(await store.FindAsync(original.Id, Token)));
        Assert.Equal(3, await database.Query("SELECT count(*)::integer FROM public.expense_changes").GetSingleAsync<int>(Token));
        Assert.Equal(3, await database.Query("SELECT count(*)::integer FROM public.expense_audit_markers").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task ApplyAsync_ConcurrentEditsFromSameOriginal_RecordsOneWholeChange() => WithStore(async (store, database) =>
    {
        var created = Create();
        var original = created.After!;
        Ok(await store.ApplyAsync(Guid.NewGuid(), created, Token));
        var first = Ok(original.Edit("Первое", Ok(Money.Create(200)), original.Date, null, null, Super, Now.AddMinutes(1)));
        var second = Ok(original.Edit("Второе", Ok(Money.Create(300)), original.Date, null, null, Super, Now.AddMinutes(1)));

        var results = await Task.WhenAll(store.ApplyAsync(Guid.NewGuid(), first, Token), store.ApplyAsync(Guid.NewGuid(), second, Token));

        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => result.IsFailure);
        Assert.Equal(2, await database.Query("SELECT count(*)::integer FROM public.expense_changes").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task ApplyAsync_HistoryWriteFails_RollsBackExpenseCreation() => WithStore(async (store, database) =>
    {
        await database.Query("""
            CREATE FUNCTION public.reject_expense_change() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Rejected history'; END; $$;
            CREATE TRIGGER reject_expense_change BEFORE INSERT ON public.expense_changes
            FOR EACH ROW EXECUTE FUNCTION public.reject_expense_change();
            """).ExecuteAsync(Token);
        var change = Create();

        await Assert.ThrowsAsync<PostgresException>(() => store.ApplyAsync(Guid.NewGuid(), change, Token));

        Assert.Null(Ok(await store.FindAsync(change.After!.Id, Token)));
    });

    [Fact]
    public Task ApplyAsync_BlockedAuthor_DeniesReplayAndNewWrite() => WithStore(async (store, database) =>
    {
        var change = Create();
        var commandId = Guid.NewGuid();
        Ok(await store.ApplyAsync(commandId, change, Token));
        await database.Query("UPDATE public.staff_access_state SET is_blocked=true WHERE staff_id=@id")
            .WithParameter("id", change.AuthorId.Value).ExecuteAsync(Token);

        var replay = await store.ApplyAsync(commandId, change, Token);
        var write = await store.ApplyAsync(Guid.NewGuid(), Create(), Token);

        Assert.Contains(replay.Error.Issues, issue => issue.Error is ModelError.Forbidden);
        Assert.Contains(write.Error.Issues, issue => issue.Error is ModelError.Forbidden);
        Assert.Equal(1, await database.Query("SELECT count(*)::integer FROM public.expense_changes").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task ApplyAsync_UnknownCategory_ReturnsValidationWithoutSaving() => WithStore(async (store, database) =>
    {
        var change = Ok(ExpenseOperations.Create(new(Guid.NewGuid()), "Материалы", Ok(Money.Create(10)),
            DateOnly.FromDateTime(Now.UtcDateTime), new ExpenseCategoryId(Guid.NewGuid()), Super, Now));

        var result = await store.ApplyAsync(Guid.NewGuid(), change, Token);

        Assert.Contains(result.Error.Issues, issue => issue.Field == "categoryId");
        Assert.Equal(0, await database.Query("SELECT count(*)::integer FROM public.expenses").GetSingleAsync<int>(Token));
    });

    private static ExpenseChange Create() => Ok(ExpenseOperations.Create(new(Guid.NewGuid()), "Материалы",
        Ok(Money.Create(123.45m)), DateOnly.FromDateTime(Now.UtcDateTime), null, Super, Now));

    [Fact]
    public Task EditAsync_ReplayAfterLaterCorrection_PreservesCurrentStateAndOriginalTime() => WithStore(async (store, database) =>
    {
        var service = new ExpenseService(store);
        var id = Guid.NewGuid();
        var original = new ExpenseValues("Материалы", 123.45m, new(2026, 11, 8), null);
        var first = original with { Amount = 200 };
        var second = original with { Amount = 300 };
        Ok(await service.CreateAsync(id, original, Super, Now, Token));
        var commandId = Guid.NewGuid();
        var corrected = Ok(await service.EditAsync(new(id), commandId, original, first, null, Super, Now.AddMinutes(1), Token));
        Ok(await service.EditAsync(new(id), Guid.NewGuid(), first, second, null, Super, Now.AddMinutes(2), Token));

        var replay = Ok(await service.EditAsync(new(id), commandId, original, first, null, Super, Now.AddMinutes(3), Token));
        var changedCommand = await service.EditAsync(new(id), commandId, original, second, null, Super, Now.AddMinutes(4), Token);

        Assert.Equal(corrected, replay);
        Assert.True(changedCommand.IsFailure);
        Assert.Equal(300m, Ok(await store.FindAsync(new(id), Token))!.Amount.Amount);
        Assert.Equal(3, await database.Query("SELECT count(*)::integer FROM public.expense_changes").GetSingleAsync<int>(Token));
    });

    [Fact]
    public Task ApplyAsync_RoleChangedToAdministrator_RequiresCommentFromCurrentRole() => WithStore(async (store, database) =>
    {
        var today = await database.Query("SELECT to_char(CURRENT_TIMESTAMP AT TIME ZONE 'Europe/Moscow', 'YYYY-MM-DD')")
            .GetSingleAsync<string>(Token);
        var created = Ok(ExpenseOperations.Create(new(Guid.NewGuid()), "Материалы", Ok(Money.Create(10)),
            DateOnly.ParseExact(today, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), null, Super, Now));
        Ok(await store.ApplyAsync(Guid.NewGuid(), created, Token));
        var original = Assert.IsType<Expense>(created.After);
        var edit = Ok(original.Edit("Исправление", Ok(Money.Create(20)), original.Date, null, null, Super, Now));
        await database.Query("UPDATE public.staff_access_state SET role='administrator' WHERE staff_id=@id")
            .WithParameter("id", edit.AuthorId.Value).ExecuteAsync(Token);

        var result = await store.ApplyAsync(Guid.NewGuid(), edit, Token);

        Assert.Contains(result.Error.Issues, issue => issue.Field == "comment");
        Assert.Equal(original, Ok(await store.FindAsync(original.Id, Token)));
    });

    private async Task WithStore(Func<ExpenseStore, Database, Task> scenario)
    {
        await using var dataSource = NpgsqlDataSource.Create(await fixture.CreateDatabaseAsync(Token));
        var database = new Database(dataSource);
        foreach (var migration in new[] { "0001_CreateClients.sql", "0003_CreateClientNotesHistory.sql",
            "0006_CreateExpenses.sql", "0008_CreateStaffPermissions.sql", "0009_CreateExpenseAuditMarkers.sql" })
            await database.Query(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", migration), Token)).ExecuteAsync(Token);
        await database.Query("INSERT INTO public.staff_access_state(staff_id, role) VALUES (@id, 'superuser')")
            .WithParameter("id", ((Actor.Staff)Super).Id.Value).ExecuteAsync(Token);
        await scenario(new ExpenseStore(database), database);
    }

    private static T Ok<T>(Result<T, ErrorList> result)
    {
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
