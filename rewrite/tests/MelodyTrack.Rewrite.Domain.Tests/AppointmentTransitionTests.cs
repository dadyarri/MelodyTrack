using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class AppointmentTransitionTests
{
    [Fact]
    public void Complete_AtEndBoundary_SetsResultIdempotently()
    {
        var planned = MakeAppointment(30, Utc(5, 15));
        var completed = Ok(planned.Complete(StaffRole.Administrator, Utc(5, 16), "Проведено", TestData.StaffId));
        var repeated = Ok(completed.Complete(StaffRole.Administrator, Utc(5, 16)));
        Assert.Equal(AppointmentStatus.Completed, repeated.Status);
        Assert.Equal(planned.Id, repeated.Id);
        Assert.Equal(completed, repeated);
    }
    [Fact]
    public void Complete_BeforeEnd_RejectsFutureResult() => AssertError(new AppointmentError.ResultInFuture(), MakeAppointment(30, Utc(5, 15)).Complete(StaffRole.Superuser, Utc(5, 16).AddTicks(-1)));
    [Fact]
    public void Complete_Teacher_RejectsEvenOwnAppointment() => AssertError(new AppointmentError.Forbidden(), MakeAppointment(30, Utc(5, 15)).Complete(StaffRole.Teacher, Utc(5, 16)));
    [Fact]
    public void Complete_PreviousSchoolMonth_RequiresSuperuser()
    {
        var planned = MakeAppointment(30, Utc(5, 15));
        var february = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero);
        AssertError(new AppointmentError.OutsideEditableMonth(), planned.Complete(StaffRole.Administrator, february));
        Assert.True(planned.Complete(StaffRole.Superuser, february, "Поздняя отметка", TestData.StaffId).IsSuccess);
    }
    [Fact]
    public void Complete_UtcPreviousMonthButSameMoscowMonth_AllowsAdministrator() => Assert.True(MakeAppointment(30, Utc(31, 22)).Complete(StaffRole.Administrator, new(2026, 2, 1, 1, 0, 0, TimeSpan.Zero), "Проведено", TestData.StaffId).IsSuccess);
    [Fact]
    public void Burn_CompletedAppointment_RequiresClearingResult() => AssertError(new AppointmentError.InvalidState(), Ok(MakeAppointment(30, Utc(5, 15)).Complete(StaffRole.Superuser, Utc(5, 16), "Проведено", TestData.StaffId)).Burn(StaffRole.Superuser, Utc(5, 16)));
    [Fact]
    public void Move_CompletedAppointment_RequiresClearingResult()
    {
        var completed = Ok(MakeAppointment(30, Utc(5, 15)).Complete(StaffRole.Superuser, Utc(5, 16), "Проведено", TestData.StaffId));
        AssertError(new AppointmentError.ClearResultFirst(), completed.Move(StaffRole.Superuser, Utc(5, 16), Utc(6, 15)));
        var moved = Ok(Ok(completed.ClearResult(StaffRole.Superuser, Utc(5, 16), "Ошибка даты", TestData.StaffId)).Move(StaffRole.Superuser, Utc(5, 16), Utc(6, 15)));
        Assert.Equal(completed.Id, moved.Id);
        Assert.Equal(AppointmentStatus.Scheduled, moved.Status);
    }
    [Fact]
    public void Move_AdministratorMovesIntoPreviousMonth_RejectsDestination() => AssertError(new AppointmentError.OutsideEditableMonth(),
        MakeAppointment(30, Utc(5, 15)).Move(StaffRole.Administrator, Now, new(2025, 12, 5, 15, 0, 0, TimeSpan.Zero)));
    [Fact]
    public void Move_CancelledAppointment_RequiresSeparateRestoration() => AssertError(new AppointmentError.InvalidState(), Ok(MakeAppointment(30, Utc(5, 15)).Cancel(StaffRole.Superuser, Now)).Move(StaffRole.Superuser, Now, Utc(6, 15)));
    [Fact]
    public void Move_SeriesAppointment_PreservesOriginalPositionAndMarksException()
    {
        var planned = SeriesAppointment(30, 3, Utc(5, 15));
        var moved = Ok(planned.Move(StaffRole.Administrator, Now, Utc(20, 15)));
        var membership = Assert.IsType<SeriesMembership>(moved.Series);
        Assert.Equal(3L, membership.SequenceNumber.Value);
        Assert.Contains(AppointmentOverride.StartTime, membership.Overrides);
        Assert.Equal(planned.Id, moved.Id);
    }
    [Fact]
    public void ApplySeriesChange_EarlierPositionMovedLater_RemainsOutsideScope()
    {
        var moved = Ok(SeriesAppointment(30, 3, Utc(5, 15)).Move(StaffRole.Administrator, Now, Utc(20, 15)));
        var updated = Ok(moved.ApplySeriesChange(StaffRole.Administrator, Now, MakeSeries().Id, Number(5), new(Utc(12, 16), null, new RoomChange.Keep())));
        Assert.Equal(moved, updated);
    }
    [Fact]
    public void ApplySeriesChange_IndividualTimeOverride_PreservesTimeWhileChangingOtherFields()
    {
        var moved = Ok(SeriesAppointment(30, 6, Utc(12, 15)).Move(StaffRole.Administrator, Now, Utc(6, 15)));
        var staff = new StaffId(Guid(31));
        var updated = Ok(moved.ApplySeriesChange(StaffRole.Administrator, Now, MakeSeries().Id, Number(5), new(Utc(15, 16), staff, new RoomChange.Set(null))));
        Assert.Equal(Utc(6, 15), updated.StartsAt);
        Assert.Equal(staff, updated.StaffId);
        Assert.Null(updated.RoomId);
    }
    [Fact]
    public void ApplySeriesChange_SelectedException_ReceivesNewValueAndBecomesPlanDefault()
    {
        var moved = Ok(SeriesAppointment(30, 5, Utc(12, 15)).Move(StaffRole.Administrator, Now, Utc(6, 15)));
        var updated = Ok(moved.ApplySeriesChange(StaffRole.Administrator, Now, MakeSeries().Id, Number(5), new(Utc(15, 16), null, new RoomChange.Keep())));
        Assert.Equal(Utc(15, 16), updated.StartsAt);
        Assert.DoesNotContain(AppointmentOverride.StartTime, Assert.IsType<SeriesMembership>(updated.Series).Overrides);
    }
    [Fact]
    public void Cancel_ManualCancellation_StillCountsTowardSeriesTarget()
    {
        var cancelled = Ok(SeriesAppointment(30, 1, Utc(5, 15)).Cancel(StaffRole.Administrator, Now));
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
        Assert.True(cancelled.CountsTowardSeriesTarget());
        Assert.IsType<CancellationReason.Manual>(Assert.Single(cancelled.CancellationReasons));
    }
    [Fact]
    public void Delete_BurnedAppointment_RequiresClearingFinancialResult()
    {
        var burned = Ok(MakeAppointment(30, Utc(5, 15)).Burn(StaffRole.Superuser, Utc(5, 16), "Неявка", TestData.StaffId));
        AssertError(new AppointmentError.ClearResultFirst(), burned.Delete(StaffRole.Superuser, Utc(5, 16)));
        Assert.True(burned.CountsTowardSeriesTarget());
    }
    [Fact]
    public void Delete_UnbilledAppointment_PreservesIdentityAndExcludesFromSeriesCount()
    {
        var planned = SeriesAppointment(30, 1, Utc(5, 15));
        var deleted = Ok(planned.Delete(StaffRole.Superuser, Now));
        Assert.True(deleted.IsDeleted);
        Assert.Equal(planned.Id, deleted.Id);
        Assert.False(deleted.CountsTowardSeriesTarget());
        AssertError(new AppointmentError.Deleted(), deleted.Move(StaffRole.Superuser, Now, Utc(6, 15)));
    }
    [Fact]
    public void Finish_OneSeries_CancelsOnlyItsPlannedFutureAppointments()
    {
        var previous = SeriesAppointment(30, 1, Utc(5, 15));
        var future = SeriesAppointment(31, 2, Utc(12, 15));
        var standalone = MakeAppointment(32, Utc(12, 15));
        var finished = Ok(SeriesEffects.Finish(Date(12), MakeSeries(), [previous, future, standalone]));
        Assert.Equal(Date(12), finished.Series.TerminatedFrom);
        Assert.Equal(AppointmentStatus.Scheduled, finished.Appointments[0].Status);
        Assert.Equal(AppointmentStatus.Cancelled, finished.Appointments[1].Status);
        Assert.Equal(AppointmentStatus.Scheduled, finished.Appointments[2].Status);
    }
}
