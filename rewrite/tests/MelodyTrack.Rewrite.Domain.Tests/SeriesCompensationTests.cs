
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class SeriesCompensationTests
{
    private static (Series Series, Appointment Original, Appointment Tail) Setup()
    {
        var series = MakeSeries(new SeriesEnd.AfterAppointments(Ok(AppointmentCount.Create(8))));
        var original = Ok(Appointment.CreateFromSeries(new(Guid(110)), series, Number(5), Utc(12, 15)))
            .CancelForAbsence(new AbsenceCause.Vacation(new(Guid(111))));
        var tail = Ok(Appointment.CreateCompensation(new(Guid(112)), series, Number(9), Utc(26, 15), original));
        return (series, original, tail);
    }
    [Fact]
    public void CreateCompensation_AutomaticCancellation_PreservesOriginalReference()
    {
        var (_, original, tail) = Setup();
        var membership = Assert.IsType<SeriesMembership>(tail.Series);
        Assert.Equal(Assert.IsType<SeriesMembership>(original.Series).SequenceNumber, membership.Compensates);
        Assert.Equal(9L, membership.SequenceNumber.Value);
    }
    [Fact]
    public void RestoreWithReplacement_OrdinaryTail_PreservesTargetCountWithoutExtraAppointment()
    {
        var (series, original, tail) = Setup();
        var before = SeriesEffects.CountedAppointments(series, [original, tail]);
        var restored = Ok(SeriesEffects.RestoreWithReplacement(StaffRole.Administrator, Now, false, original, tail));
        Assert.Equal(AppointmentStatus.Scheduled, restored.Original.Status);
        Assert.Equal(AppointmentStatus.Cancelled, restored.Replacement.Status);
        Assert.Equal(before, SeriesEffects.CountedAppointments(series, [restored.Original, restored.Replacement]));
        Assert.Contains(new CancellationReason.CompensationReplaced(), restored.Replacement.CancellationReasons);
    }
    [Fact]
    public void RestoreWithReplacement_CompletedTail_RequiresExplicitRuleInsteadOfGuessing()
    {
        var (_, original, tail) = Setup();
        var completed = Ok(tail.Complete(StaffRole.Superuser, Utc(26, 16)));
        AssertError(new AppointmentError.ReplacementRequiresReview(), SeriesEffects.RestoreWithReplacement(StaffRole.Superuser, Utc(26, 16), false, original, completed));
        Assert.Equal(AppointmentStatus.Cancelled, original.Status);
    }
    [Fact]
    public void RestoreWithReplacement_IndividuallyMovedTail_RequiresReview()
    {
        var (_, original, tail) = Setup();
        var moved = Ok(tail.Move(StaffRole.Superuser, Now, Utc(27, 15)));
        AssertError(new AppointmentError.ReplacementRequiresReview(), SeriesEffects.RestoreWithReplacement(StaffRole.Superuser, Now, false, original, moved));
    }
    [Fact]
    public void CreateCompensation_ManualCancellation_DoesNotAddReplacement()
    {
        var series = MakeSeries(new SeriesEnd.AfterAppointments(Ok(AppointmentCount.Create(8))));
        var original = Ok(Appointment.CreateFromSeries(new(Guid(110)), series, SequenceNumber.First, Utc(5, 15)));
        var cancelled = Ok(original.Cancel(StaffRole.Superuser, Now));
        AssertError(new AppointmentError.CompensationNotNeeded(), Appointment.CreateCompensation(new(Guid(112)), series, Number(9), Utc(26, 15), cancelled));
    }
    [Fact]
    public void CountedAppointments_RepeatedPosition_IsNotCountedTwice()
    {
        var (series, _, tail) = Setup();
        Assert.Equal(1, SeriesEffects.CountedAppointments(series, [tail, tail]));
    }
}
