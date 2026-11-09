using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class EmployeeTrainingServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Actor Owner => new Actor.Staff(TestData.StaffId, StaffRole.Teacher);
    private static EmployeeTrainingScenario Scenario(int version = 1) => Ok(EmployeeTrainingRules.CreateScenario("demo", version,
        ImmutableDictionary<string, string>.Empty.Add("payment", "absent"),
        [new("demo-payment", ImmutableDictionary<string, string>.Empty.Add("payment", "created"))]));

    [Fact]
    public async Task ApplyAsync_FormClosesAfterSkippedStart_PersistsRunningTraining()
    {
        var store = new MemoryStore();
        var service = new EmployeeTrainingService(store, new Access(true));
        var skipped = Ok(await service.ApplyAsync(MakeStaff(), Owner, Scenario(), 0,
            new EmployeeTrainingChange.AutoStart(EmployeeTrainingAutoStart.FirstSuccessfulSignIn, true), Token));

        var started = Ok(await service.ApplyAsync(MakeStaff(), Owner, Scenario(), skipped.Revision,
            new EmployeeTrainingChange.AutoStart(EmployeeTrainingAutoStart.WorkFormClosed, false), Token));

        Assert.True(skipped.Progress.AutoStartSkippedForOpenForm);
        Assert.False(skipped.Progress.IsRunning);
        Assert.True(started.Progress.BlocksOrdinaryWork);
        Assert.False(started.Progress.AutoStartSkippedForOpenForm);
        Assert.Equal(started, store.Progress);
    }

    [Fact]
    public async Task ApplyAsync_UpdatedDeferredPart_PersistsResetAndKeepsDeferral()
    {
        var store = new MemoryStore();
        var service = new EmployeeTrainingService(store, new Access(true));
        var started = Ok(await service.ApplyAsync(MakeStaff(), Owner, Scenario(), 0, new EmployeeTrainingChange.StartManually(), Token));
        var changed = Ok(await service.ApplyAsync(MakeStaff(), Owner, Scenario(), started.Revision, new EmployeeTrainingChange.SetDemoValue("payment", "created"), Token));
        var deferred = Ok(await service.ApplyAsync(MakeStaff(), Owner, Scenario(), changed.Revision, new EmployeeTrainingChange.Defer(), Token));

        var updated = Ok(await service.ApplyAsync(MakeStaff(), Owner, Scenario(2), deferred.Revision,
            new EmployeeTrainingChange.RefreshVersion(), Token));

        Assert.Equal(2, updated.Progress.Version);
        Assert.Equal(0, updated.Progress.NextStep);
        Assert.Equal("absent", updated.Progress.DemoState["payment"]);
        Assert.True(updated.Progress.IsDeferred);
        Assert.False(updated.Progress.BlocksOrdinaryWork);
        Assert.Equal(updated, store.Progress);
    }

    [Fact]
    public async Task ApplyAsync_DeferredProgress_PersistsSuppressionAndState()
    {
        var store = new MemoryStore();
        var service = new EmployeeTrainingService(store, new Access(true));
        var started = Ok(await service.ApplyAsync(MakeStaff(), Owner, Scenario(), 0, new EmployeeTrainingChange.StartManually(), Token));
        var changed = Ok(await service.ApplyAsync(MakeStaff(), Owner, Scenario(), started.Revision, new EmployeeTrainingChange.SetDemoValue("payment", "created"), Token));

        var deferred = Ok(await service.ApplyAsync(MakeStaff(), Owner, Scenario(), changed.Revision, new EmployeeTrainingChange.Defer(), Token));

        Assert.Equal("created", deferred.Progress.DemoState["payment"]);
        Assert.True(deferred.Progress.WasDeferred);
        Assert.False(deferred.Progress.IsRunning);
        Assert.Equal(deferred, Ok(await service.ReadProgressAsync(MakeStaff(), Owner, Scenario().Id, Token)));
    }

    [Fact]
    public async Task ApplyAsync_StaleRevision_DoesNotReplayCommandAgainstNewerState()
    {
        var store = new MemoryStore();
        var service = new EmployeeTrainingService(store, new Access(true));
        var started = Ok(await service.ApplyAsync(MakeStaff(), Owner, Scenario(), 0, new EmployeeTrainingChange.StartManually(), Token));
        var changed = Ok(await service.ApplyAsync(MakeStaff(), Owner, Scenario(), started.Revision, new EmployeeTrainingChange.SetDemoValue("payment", "created"), Token));

        var result = await service.ApplyAsync(MakeStaff(), Owner, Scenario(), started.Revision, new EmployeeTrainingChange.Defer(), Token);

        AssertError(new ModelError.InvalidState(), result);
        Assert.Equal(changed, store.Progress);
        Assert.Equal(2, store.Writes);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadProgressAsync_BlockedOrArchivedCurrentStaff_DeniesBeforeReadingStore(bool blocked)
    {
        var store = new MemoryStore();
        var service = new EmployeeTrainingService(store, new Access(true));
        var staff = blocked ? StaffRules.Block(MakeStaff()) : Ok(StaffRules.Archive(MakeStaff(), NoUsage, false));

        var result = await service.ReadProgressAsync(staff, Owner, Scenario().Id, Token);

        AssertError(new ModelError.Forbidden(), result);
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task ApplyAsync_DifferentAccount_DoesNotWriteAnotherEmployeesProgress()
    {
        var store = new MemoryStore();
        var service = new EmployeeTrainingService(store, new Access(true));
        var other = new Actor.Staff(new StaffId(Guid(302)), StaffRole.Superuser);

        var result = await service.ApplyAsync(MakeStaff(), other, Scenario(), 0, new EmployeeTrainingChange.StartManually(), Token);

        AssertError(new ModelError.Forbidden(), result);
        Assert.Equal(0, store.Reads);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task ApplyAsync_RevokedContentAccess_PreservesStoredProgress()
    {
        var store = new MemoryStore();
        var access = new Access(true);
        var service = new EmployeeTrainingService(store, access);
        var started = Ok(await service.ApplyAsync(MakeStaff(), Owner, Scenario(), 0, new EmployeeTrainingChange.StartManually(), Token));
        access.Allowed = false;

        var result = await service.ApplyAsync(MakeStaff(), Owner, Scenario(), started.Revision, new EmployeeTrainingChange.SetDemoValue("payment", "created"), Token);

        AssertError(new ModelError.Forbidden(), result);
        Assert.Equal(started, store.Progress);
        Assert.Equal(1, store.Writes);
    }

    [Fact]
    public async Task OpenArticleAsync_CurrentVersion_ReadsArticleWithoutCompletingTraining()
    {
        var store = new MemoryStore();
        var service = new EmployeeTrainingService(store, new Access(true));

        var opened = Ok(await service.OpenArticleAsync(MakeStaff(), Owner, "guide", 2, 0, Token));

        Assert.Equal(2, opened.Progress.ReadVersion);
        Assert.False(opened.Progress.HasNewUpdate);
        Assert.Null(store.Progress);
    }

    [Fact]
    public async Task RefreshArticleVersionAsync_UpdatedArticle_PersistsNewUntilOpened()
    {
        var store = new MemoryStore();
        var service = new EmployeeTrainingService(store, new Access(true));
        var first = Ok(await service.OpenArticleAsync(MakeStaff(), Owner, "guide", 1, 0, Token));

        var changed = Ok(await service.RefreshArticleVersionAsync(MakeStaff(), Owner, "guide", 2, first.Revision, Token));
        var listed = Ok(await service.ReadArticleAsync(MakeStaff(), Owner, "guide", Token));
        var opened = Ok(await service.OpenArticleAsync(MakeStaff(), Owner, "guide", 2, changed.Revision, Token));

        Assert.NotNull(listed);
        Assert.True(listed.Progress.HasNewUpdate);
        Assert.False(opened.Progress.HasNewUpdate);
        Assert.Null(store.Progress);
    }

    [Fact]
    public async Task ApplyAsync_Cancelled_DoesNotReadOrWrite()
    {
        var store = new MemoryStore();
        var service = new EmployeeTrainingService(store, new Access(true));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ApplyAsync(MakeStaff(), Owner, Scenario(), 0, new EmployeeTrainingChange.StartManually(), cancellation.Token));

        Assert.Equal(0, store.Reads);
        Assert.Equal(0, store.Writes);
    }

    private sealed class Access(bool allowed) : IEmployeeTrainingAccess
    {
        internal bool Allowed = allowed;
        public Task<bool> CanUseScenarioAsync(StaffId staffId, string scenarioId, CancellationToken cancellationToken) => Task.FromResult(Allowed);
        public Task<bool> CanReadArticleAsync(StaffId staffId, string articleId, CancellationToken cancellationToken) => Task.FromResult(Allowed);
    }

    private sealed class MemoryStore : IEmployeeTrainingStore
    {
        internal StoredEmployeeTrainingProgress? Progress;
        internal StoredEmployeeTrainingArticle? Article;
        internal int Reads;
        internal int Writes;

        public Task<Result<StoredEmployeeTrainingProgress?, ErrorList>> ReadProgressAsync(StaffId staffId, string scenarioId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Result.Success<StoredEmployeeTrainingProgress?, ErrorList>(Progress));
        }

        public Task<Result<StoredEmployeeTrainingProgress, ErrorList>> SaveProgressAsync(EmployeeTrainingProgress progress, long expectedRevision, CancellationToken cancellationToken)
        {
            Writes++;
            Progress = new(progress, expectedRevision + 1);
            return Task.FromResult(Result.Success<StoredEmployeeTrainingProgress, ErrorList>(Progress));
        }

        public Task<Result<StoredEmployeeTrainingArticle?, ErrorList>> ReadArticleAsync(StaffId staffId, string articleId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Result.Success<StoredEmployeeTrainingArticle?, ErrorList>(Article));
        }

        public Task<Result<StoredEmployeeTrainingArticle, ErrorList>> SaveArticleAsync(EmployeeTrainingArticleProgress progress, long expectedRevision, CancellationToken cancellationToken)
        {
            Writes++;
            Article = new(progress, expectedRevision + 1);
            return Task.FromResult(Result.Success<StoredEmployeeTrainingArticle, ErrorList>(Article));
        }
    }
}
