using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class PracticeAttemptTests
{
    private static Actor Owner => new Actor.Staff(TestData.StaffId, StaffRole.Teacher);
    private static Exercise Exercise() => Ok(global::MelodyTrack.Rewrite.Domain.Exercise.Create(new(Guid(100)),
        Ok(TrainerConfiguration.Create("rhythm", 120, true, true, TimeSpan.FromSeconds(4), [])), null));

    [Fact]
    public void Stop_PausedAttempt_ExcludesPauseFromDuration()
    {
        var attempt = Ok(PracticeAttemptRules.Start(new(Guid(101)), Exercise(), new PracticeContext.Free(Owner), Utc(1, 15)));
        attempt = Ok(attempt.Pause(Utc(1, 15).AddSeconds(10), 2));
        attempt = Ok(attempt.Resume(Utc(1, 15).AddSeconds(50)));

        var stopped = Ok(attempt.Stop(Utc(1, 15).AddSeconds(55), 3));

        Assert.Equal(TimeSpan.FromSeconds(15), stopped.ActiveDuration);
        Assert.Equal(3, stopped.Repetitions);
        Assert.Equal(PracticeState.Finished, stopped.State);
    }

    [Fact]
    public void CanRead_AnotherSuperuser_DoesNotExposeFreeHistory()
    {
        var attempt = Ok(PracticeAttemptRules.Start(new(Guid(101)), Exercise(), new PracticeContext.Free(Owner), Utc(1, 15)));

        Assert.False(attempt.CanRead(new Actor.Staff(new(Guid(102)), StaffRole.Superuser)));
        Assert.True(attempt.CanRead(new Actor.Staff(TestData.StaffId, StaffRole.Superuser)));
    }

    [Fact]
    public void Interrupt_UnreachedGoal_PreservesAttemptAndConfiguration()
    {
        var exercise = Exercise();
        var attempt = Ok(PracticeAttemptRules.Start(new(Guid(101)), exercise, new PracticeContext.Free(Owner), Utc(1, 15)));

        var interrupted = Ok(attempt.Interrupt(Utc(1, 15).AddSeconds(5), 1));

        Assert.Equal(PracticeState.Interrupted, interrupted.State);
        Assert.Equal(exercise, interrupted.Exercise);
        Assert.Equal(TimeSpan.FromSeconds(4), Ok(interrupted.VideoDuration(Owner)));
        AssertError(new ModelError.InvalidState(), interrupted.CopyFreeConfiguration(Owner, true));
    }
}
