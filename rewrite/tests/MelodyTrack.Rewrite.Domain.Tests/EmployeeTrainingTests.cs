using System.Collections.Immutable;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class EmployeeTrainingTests
{
    private static ImmutableHashSet<string> Available => ["payments-demo"];
    private static Actor Teacher => new Actor.Staff(TestData.StaffId, StaffRole.Teacher);
    private static EmployeeTrainingScenario Scenario(int version = 1) => Ok(EmployeeTrainingRules.CreateScenario("payments-demo", version,
        ImmutableDictionary<string, string>.Empty.Add("payment", "absent"),
        [new("record-payment", ImmutableDictionary<string, string>.Empty.Add("payment", "recorded"))]));

    private static EmployeeTrainingProgress Progress(EmployeeTrainingScenario scenario) => Ok(EmployeeTrainingRules.CreateProgress(TestData.StaffId, scenario));

    [Theory]
    [InlineData(StaffRole.Teacher)]
    [InlineData(StaffRole.Administrator)]
    [InlineData(StaffRole.Superuser)]
    public void AutoStart_FirstSuccessfulStaffSignIn_BlocksWork(StaffRole role)
    {
        var progress = Progress(Scenario());

        var started = Ok(progress.AutoStart(new Actor.Staff(TestData.StaffId, role), Available, EmployeeTrainingAutoStart.FirstSuccessfulSignIn, false));

        Assert.Equal(EmployeeTrainingStatus.InProgress, started.Status);
        Assert.True(started.BlocksOrdinaryWork);
    }

    [Fact]
    public void AutoStart_Client_DeniesEmployeeTraining()
    {
        var result = Progress(Scenario()).AutoStart(new Actor.Client(TestData.ClientId), Available, EmployeeTrainingAutoStart.FirstSuccessfulSignIn, false);

        AssertError(new ModelError.Forbidden(), result);
    }

    [Fact]
    public void StartManually_OtherStaffAccount_DoesNotShareProgress()
    {
        var result = Progress(Scenario()).StartManually(new Actor.Staff(new StaffId(Guid(300)), StaffRole.Superuser), Available);

        AssertError(new ModelError.Forbidden(), result);
    }

    [Fact]
    public void CompleteStep_WithoutExpectedDemoState_DoesNotAdvance()
    {
        var scenario = Scenario();
        var started = Ok(Progress(scenario).StartManually(Teacher, Available));

        var result = started.CompleteStep(Teacher, Available, scenario);

        AssertError(new ModelError.InvalidState(), result);
        Assert.Equal(0, started.NextStep);
        Assert.Null(started.CompletedVersion);
    }

    [Fact]
    public void CompleteStep_DemoAction_CompletesOnlyIsolatedTrainingState()
    {
        var scenario = Scenario();
        var started = Ok(Progress(scenario).StartManually(Teacher, Available));

        var changed = Ok(started.ApplyDemoValue(Teacher, Available, "payment", "recorded"));
        var finished = Ok(changed.CompleteStep(Teacher, Available, scenario));

        Assert.Equal("absent", scenario.InitialState["payment"]);
        Assert.Equal("absent", started.DemoState["payment"]);
        Assert.Equal("recorded", finished.DemoState["payment"]);
        Assert.Equal(EmployeeTrainingStatus.Completed, finished.Status);
        Assert.Equal(1, finished.CompletedVersion);
        Assert.False(finished.BlocksOrdinaryWork);
    }

    [Fact]
    public void Defer_NextWorkday_DoesNotStartAgainAndAllowsManualContinuation()
    {
        var started = Ok(Progress(Scenario()).AutoStart(Teacher, Available, EmployeeTrainingAutoStart.FirstSuccessfulSignIn, false));
        var deferred = Ok(started.Defer(Teacher, Available));

        var nextDay = Ok(deferred.AutoStart(Teacher, Available, EmployeeTrainingAutoStart.WorkdayStarted, false));
        var resumed = Ok(nextDay.StartManually(Teacher, Available));

        Assert.Equal(deferred, nextDay);
        Assert.True(deferred.IsDeferred);
        Assert.False(nextDay.BlocksOrdinaryWork);
        Assert.Equal(EmployeeTrainingStatus.InProgress, nextDay.Status);
        Assert.True(resumed.BlocksOrdinaryWork);
        Assert.True(resumed.WasDeferred);
    }

    [Fact]
    public void AutoStart_OpenWorkForm_StartsWhenFormCloses()
    {
        var progress = Progress(Scenario());

        var skipped = Ok(progress.AutoStart(Teacher, Available, EmployeeTrainingAutoStart.FirstSuccessfulSignIn, true));
        var retry = Ok(skipped.AutoStart(Teacher, Available, EmployeeTrainingAutoStart.WorkFormClosed, false));

        Assert.False(skipped.IsRunning);
        Assert.False(skipped.BlocksOrdinaryWork);
        Assert.True(skipped.AutoStartSkippedForOpenForm);
        Assert.True(retry.IsRunning);
        Assert.True(retry.BlocksOrdinaryWork);
        Assert.False(retry.AutoStartSkippedForOpenForm);
        Assert.Equal(retry, Ok(retry.AutoStart(Teacher, Available, EmployeeTrainingAutoStart.WorkFormClosed, false)));
    }

    [Fact]
    public void AutoStart_FormClosedWithoutSkippedStart_DoesNotLaunchTraining()
    {
        var progress = Progress(Scenario());

        var closed = Ok(progress.AutoStart(Teacher, Available, EmployeeTrainingAutoStart.WorkFormClosed, false));

        Assert.Equal(progress, closed);
        Assert.False(closed.BlocksOrdinaryWork);
    }

    [Fact]
    public void AutoStart_FormStillOpen_KeepsSkippedStartPending()
    {
        var progress = Progress(Scenario());
        var skipped = Ok(progress.AutoStart(Teacher, Available, EmployeeTrainingAutoStart.FirstSuccessfulSignIn, true));

        var stillOpen = Ok(skipped.AutoStart(Teacher, Available, EmployeeTrainingAutoStart.WorkFormClosed, true));

        Assert.Equal(skipped, stillOpen);
        Assert.False(stillOpen.BlocksOrdinaryWork);
    }

    [Fact]
    public void ApplyVersion_CompletedPart_RequiresChangedPartAtWorkdayStart()
    {
        var scenario = Scenario();
        var started = Ok(Progress(scenario).StartManually(Teacher, Available));
        var finished = Ok(Ok(started.ApplyDemoValue(Teacher, Available, "payment", "recorded")).CompleteStep(Teacher, Available, scenario));

        var unchanged = Ok(finished.ApplyVersion(scenario));
        var updated = Ok(finished.ApplyVersion(Scenario(2)));
        var deployment = Ok(updated.AutoStart(Teacher, Available, EmployeeTrainingAutoStart.None, false));
        var workday = Ok(deployment.AutoStart(Teacher, Available, EmployeeTrainingAutoStart.WorkdayStarted, false));

        Assert.Equal(finished, unchanged);
        Assert.Equal(1, updated.CompletedVersion);
        Assert.True(updated.HasRequiredUpdate);
        Assert.False(deployment.IsRunning);
        Assert.True(workday.BlocksOrdinaryWork);
    }

    [Fact]
    public void ApplyVersion_UnfinishedPart_RestartsOnlyChangedPartOnNewVersion()
    {
        var old = Ok(EmployeeTrainingRules.CreateScenario("payments-demo", 1,
            ImmutableDictionary<string, string>.Empty.Add("payment", "absent"),
            [new("record-payment", ImmutableDictionary<string, string>.Empty.Add("payment", "recorded")),
             new("correct-payment", ImmutableDictionary<string, string>.Empty.Add("payment", "corrected"))]));
        var started = Ok(Progress(old).StartManually(Teacher, Available));
        var firstStep = Ok(Ok(started.ApplyDemoValue(Teacher, Available, "payment", "recorded")).CompleteStep(Teacher, Available, old));
        var other = Ok(EmployeeTrainingRules.CreateScenario("schedule-demo", 1, ImmutableDictionary<string, string>.Empty,
            [new("move", ImmutableDictionary<string, string>.Empty.Add("moved", "yes"))]));
        var otherProgress = Progress(other);

        var updated = Ok(firstStep.ApplyVersion(Scenario(2)));

        Assert.Equal(2, updated.Version);
        Assert.Equal(EmployeeTrainingStatus.InProgress, updated.Status);
        Assert.Equal(0, updated.NextStep);
        Assert.Equal("absent", updated.DemoState["payment"]);
        Assert.True(updated.BlocksOrdinaryWork);
        Assert.Null(updated.CompletedVersion);
        Assert.Equal(1, firstStep.NextStep);
        Assert.Equal(otherProgress, Ok(otherProgress.ApplyVersion(other)));
    }

    [Fact]
    public void ApplyVersion_DeferredUnfinishedPart_ResetsProgressWithoutAutomaticStart()
    {
        var started = Ok(Progress(Scenario()).StartManually(Teacher, Available));
        var changed = Ok(started.ApplyDemoValue(Teacher, Available, "payment", "recorded"));
        var deferred = Ok(changed.Defer(Teacher, Available));

        var updated = Ok(deferred.ApplyVersion(Scenario(2)));
        var workday = Ok(updated.AutoStart(Teacher, Available, EmployeeTrainingAutoStart.WorkdayStarted, false));
        var closed = Ok(updated.AutoStart(Teacher, Available, EmployeeTrainingAutoStart.WorkFormClosed, false));
        var resumed = Ok(updated.StartManually(Teacher, Available));

        Assert.Equal(2, updated.Version);
        Assert.Equal(0, updated.NextStep);
        Assert.Equal("absent", updated.DemoState["payment"]);
        Assert.True(updated.IsDeferred);
        Assert.False(updated.BlocksOrdinaryWork);
        Assert.Equal(updated, workday);
        Assert.Equal(updated, closed);
        Assert.True(resumed.BlocksOrdinaryWork);
        Assert.Equal(1, started.Version);
    }

    [Fact]
    public void CompleteStep_RevokedScenarioAccess_PreservesProgressAndDeniesAction()
    {
        var scenario = Scenario();
        var started = Ok(Progress(scenario).StartManually(Teacher, Available));

        var result = started.ApplyDemoValue(Teacher, [], "payment", "recorded");

        AssertError(new ModelError.Forbidden(), result);
        Assert.Equal(EmployeeTrainingStatus.InProgress, started.Status);
    }

    [Fact]
    public void OpenForReading_UpdatedArticle_ClearsNewWithoutCompletingExercise()
    {
        var exercise = Progress(Scenario());
        var article = Ok(EmployeeTrainingArticleRules.Create(TestData.StaffId, "payments-guide", 1));
        article = Ok(article.OpenForReading(Teacher, ["payments-guide"]));
        var updated = Ok(article.ApplyVersion(2));

        var read = Ok(updated.OpenForReading(Teacher, ["payments-guide"]));

        Assert.True(updated.HasNewUpdate);
        Assert.False(read.HasNewUpdate);
        Assert.Equal(2, read.ReadVersion);
        Assert.Equal(EmployeeTrainingStatus.NotStarted, exercise.Status);
        Assert.Null(exercise.CompletedVersion);
    }

    [Fact]
    public void RestoreProgress_DeferredPart_PreservesAllSubjectState()
    {
        var scenario = Scenario();
        var started = Ok(Progress(scenario).StartManually(Teacher, Available));
        var deferred = Ok(Ok(started.ApplyDemoValue(Teacher, Available, "payment", "recorded")).Defer(Teacher, Available));

        var restored = Ok(EmployeeTrainingRules.RestoreProgress(deferred.StaffId, deferred.ScenarioId, deferred.Version,
            deferred.CompletedVersion, deferred.Status, deferred.NextStep, deferred.DemoState, deferred.IsRunning,
            deferred.WasDeferred, deferred.AutoStartSkippedForOpenForm));

        Assert.Equal(deferred, restored);
        Assert.True(restored.IsDeferred);
    }

    [Fact]
    public void RestoreProgress_CompletedWithoutCompletedVersion_RejectsInconsistentState()
    {
        var result = EmployeeTrainingRules.RestoreProgress(TestData.StaffId, "payments-demo", 1, null,
            EmployeeTrainingStatus.Completed, 1, ImmutableDictionary<string, string>.Empty, false, false, false);

        AssertError(new ModelError.InvalidState(), result);
    }
}
