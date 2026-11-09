using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;
using MelodyTrack.Rewrite.Persistence;
using MelodyTrack.Rewrite.Persistence.Training;
using Npgsql;
using Xunit;

namespace MelodyTrack.Rewrite.Init.Tests;

public sealed class EmployeeTrainingStoreTests(PostgreSqlFixture fixture) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly StaffId Staff = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000001"));
    private static EmployeeTrainingScenario Scenario() => Ok(EmployeeTrainingRules.CreateScenario("demo-payment", 1,
        ImmutableDictionary<string, string>.Empty.Add("payment", "absent"),
        [new("create-demo-payment", ImmutableDictionary<string, string>.Empty.Add("payment", "created"))]));

    [Fact]
    public Task SaveProgressAsync_DeferredProgress_RestoresStateAndSuppressesAutomaticRestart() => WithStore(async (store, database) =>
    {
        var started = Ok(Ok(EmployeeTrainingRules.CreateProgress(Staff, Scenario())).StartManually());
        var changed = Ok(started.ApplyDemoValue("payment", "created"));
        var deferred = Ok(changed.Defer());

        var saved = Ok(await store.SaveProgressAsync(deferred, 0, Token));
        var loaded = Ok(await store.ReadProgressAsync(Staff, Scenario().Id, Token));

        Assert.NotNull(loaded);
        Assert.Equal(1L, saved.Revision);
        Assert.Equal(EmployeeTrainingStatus.InProgress, loaded.Progress.Status);
        Assert.True(loaded.Progress.WasDeferred);
        Assert.Equal("created", loaded.Progress.DemoState["payment"]);
        Assert.False(Ok(loaded.Progress.AutoStart(EmployeeTrainingAutoStart.WorkdayStarted, false)).IsRunning);
        var statusType = await database.Query("SELECT pg_typeof(status)::text FROM public.employee_training_progress").GetSingleAsync<string>(Token);
        Assert.Equal("employee_training_status", statusType);
    });

    [Fact]
    public Task SaveProgressAsync_IdenticalReplay_DoesNotIncrementRevision() => WithStore(async (store, _) =>
    {
        var progress = Ok(EmployeeTrainingRules.CreateProgress(Staff, Scenario()));

        var saved = Ok(await store.SaveProgressAsync(progress, 0, Token));
        var replay = Ok(await store.SaveProgressAsync(progress, 0, Token));

        Assert.Equal(saved.Revision, replay.Revision);
        Assert.Equal(progress.Status, replay.Progress.Status);
    });

    [Fact]
    public Task SaveProgressAsync_StaleReplay_DoesNotOverwriteNewerProgress() => WithStore(async (store, _) =>
    {
        var progress = Ok(EmployeeTrainingRules.CreateProgress(Staff, Scenario()));
        var original = Ok(await store.SaveProgressAsync(progress, 0, Token));
        var started = Ok(progress.StartManually());
        var newer = Ok(await store.SaveProgressAsync(started, original.Revision, Token));

        var stale = await store.SaveProgressAsync(progress, 0, Token);
        var loaded = Ok(await store.ReadProgressAsync(Staff, Scenario().Id, Token));

        Assert.True(stale.IsFailure);
        Assert.NotNull(loaded);
        Assert.Equal(newer.Revision, loaded.Revision);
        Assert.True(loaded.Progress.IsRunning);
    });

    [Fact]
    public Task SaveProgressAsync_ConcurrentDistinctChanges_OnlyOneWins() => WithStore(async (store, _) =>
    {
        var progress = Ok(Ok(EmployeeTrainingRules.CreateProgress(Staff, Scenario())).StartManually());
        var saved = Ok(await store.SaveProgressAsync(progress, 0, Token));
        var first = Ok(progress.ApplyDemoValue("payment", "created"));
        var second = Ok(progress.Defer());

        var results = await Task.WhenAll(store.SaveProgressAsync(first, saved.Revision, Token), store.SaveProgressAsync(second, saved.Revision, Token));

        Assert.Single(results, result => result.IsSuccess);
        Assert.Single(results, result => result.IsFailure);
        Assert.Equal(2L, Ok(await store.ReadProgressAsync(Staff, Scenario().Id, Token))!.Revision);
    });

    [Fact]
    public Task ReadProgressAsync_OtherAccount_DoesNotReturnOwnersProgress() => WithStore(async (store, _) =>
    {
        Ok(await store.SaveProgressAsync(Ok(EmployeeTrainingRules.CreateProgress(Staff, Scenario())), 0, Token));

        var other = Ok(await store.ReadProgressAsync(new StaffId(Guid.NewGuid()), Scenario().Id, Token));

        Assert.Null(other);
    });

    [Fact]
    public Task SaveArticleAsync_ReadUpdatedArticle_DoesNotCompleteExercise() => WithStore(async (store, _) =>
    {
        var exercise = Ok(await store.SaveProgressAsync(Ok(EmployeeTrainingRules.CreateProgress(Staff, Scenario())), 0, Token));
        var article = Ok(EmployeeTrainingArticleRules.Create(Staff, "payment-guide", 1)).OpenForReading();
        var initial = Ok(await store.SaveArticleAsync(article, 0, Token));
        var updated = Ok(article.ApplyVersion(2));
        var changed = Ok(await store.SaveArticleAsync(updated, initial.Revision, Token));

        var read = Ok(await store.SaveArticleAsync(updated.OpenForReading(), changed.Revision, Token));

        Assert.False(read.Progress.HasNewUpdate);
        Assert.Equal(2, read.Progress.ReadVersion);
        Assert.Equal(exercise.Revision, Ok(await store.ReadProgressAsync(Staff, Scenario().Id, Token))!.Revision);
        Assert.Equal(EmployeeTrainingStatus.NotStarted, Ok(await store.ReadProgressAsync(Staff, Scenario().Id, Token))!.Progress.Status);
        Assert.True((await store.SaveArticleAsync(article, 0, Token)).IsFailure);
    });

    [Fact]
    public Task ReadProgressAsync_InvalidDemoFormat_ReturnsControlledError() => WithStore(async (store, database) =>
    {
        Ok(await store.SaveProgressAsync(Ok(EmployeeTrainingRules.CreateProgress(Staff, Scenario())), 0, Token));
        await database.Query("UPDATE public.employee_training_progress SET demo_state = '{\"payment\": 123}'::jsonb").ExecuteAsync(Token);

        var result = await store.ReadProgressAsync(Staff, Scenario().Id, Token);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Issues, issue => issue.Field == "demoState");
    });

    [Fact]
    public Task SaveProgressAsync_CancelledRequest_DoesNotWrite() => WithStore(async (store, _) =>
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveProgressAsync(Ok(EmployeeTrainingRules.CreateProgress(Staff, Scenario())), 0, cancellation.Token));

        Assert.Null(Ok(await store.ReadProgressAsync(Staff, Scenario().Id, Token)));
    });

    private async Task WithStore(Func<EmployeeTrainingStore, Database, Task> scenario)
    {
        await using var source = NpgsqlDataSource.Create(await fixture.CreateDatabaseAsync(Token));
        var database = new Database(source);
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Schema", "0004_CreateEmployeeTrainingProgress.sql"), Token);
        await database.Query(sql).ExecuteAsync(Token);
        await scenario(new EmployeeTrainingStore(database), database);
    }

    private static T Ok<T>(Result<T, ErrorList> result)
    {
        Assert.True(result.IsSuccess);
        return result.Value;
    }
}
