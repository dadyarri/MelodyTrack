using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class SeriesTests
{
    [Fact]
    public void Create_EndBeforeStart_RejectsSeries() => AssertError(new SchedulingError.SeriesEndBeforeStart(Date(5), Date(4)),
        SeriesRules.Create(new(Guid(10)), TestData.ClientId, TestData.ServiceId, Duration(60), Date(5), new SeriesEnd.OnDateInclusive(Date(4)), new(Weekly, TestData.StaffId, null)));
    [Fact]
    public void NextCandidate_EndDateIsInclusive_EmitsFinalDateThenStops()
    {
        var series = MakeSeries(new SeriesEnd.OnDateInclusive(Date(5)));
        var step = Assert.IsType<SeriesStep>(Ok(series.NextCandidate(0)));
        Assert.Equal(Utc(5, 15), step.Candidate.StartsAt);
        Assert.Equal(1L, step.Candidate.SequenceNumber.Value);
        Assert.Null(Ok(step.Series.NextCandidate(1)));
    }
    [Fact]
    public void NextCandidate_SkippedCandidateDoesNotCount_ContinuesUntilTarget()
    {
        var series = MakeSeries(new SeriesEnd.AfterAppointments(Ok(AppointmentCount.Create(1))));
        var skipped = Assert.IsType<SeriesStep>(Ok(series.NextCandidate(0)));
        var accepted = Assert.IsType<SeriesStep>(Ok(skipped.Series.NextCandidate(0)));
        Assert.Equal(1L, skipped.Candidate.SequenceNumber.Value);
        Assert.Equal(2L, accepted.Candidate.SequenceNumber.Value);
        Assert.Equal(Utc(8, 16), accepted.Candidate.StartsAt);
        Assert.Null(Ok(accepted.Series.NextCandidate(1)));
    }
    [Fact]
    public void NextCandidate_NegativeCount_RejectsValue() => AssertError(new SchedulingError.InvalidCountedAppointments(), MakeSeries().NextCandidate(-1));
    [Fact]
    public void Finish_ChosenDate_StopsCandidatesStartingOnDate() => Assert.Null(Ok(Ok(MakeSeries().Finish(Date(5))).NextCandidate(0)));
    [Fact]
    public void ChangeRule_FinishedSeries_DoesNotReopenIt()
    {
        var finished = Ok(MakeSeries().Finish(Date(12)));
        AssertError(new SchedulingError.SeriesFinished(), finished.ChangeRule(finished.Rule));
        AssertError(new SchedulingError.SeriesFinished(), finished.Finish(Date(13)));
    }
    [Fact]
    public void NextCandidate_OpenEndedSeries_PreservesClientServiceAndFrozenDuration()
    {
        var original = MakeSeries();
        var step = Assert.IsType<SeriesStep>(Ok(original.NextCandidate(0)));
        Assert.Equal(original.ClientId, step.Series.ClientId);
        Assert.Equal(original.ServiceId, step.Series.ServiceId);
        Assert.Equal(original.Duration, step.Series.Duration);
        Assert.Equal(2L, step.Series.NextSequenceNumber.Value);
        Assert.Equal(1L, original.NextSequenceNumber.Value);
    }
}
