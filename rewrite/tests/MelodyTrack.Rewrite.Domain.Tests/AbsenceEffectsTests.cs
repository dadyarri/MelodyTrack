using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class AbsenceEffectsTests
{
    [Fact]
    public void ApplyVacation_PartialIntersection_RequiresResolutionWithoutCancellingAppointment()
    {
        var appointment = MakeAppointment(80, Utc(5, 15));
        var vacation = MakeVacation(81, new VacationSubject.Client(TestData.ClientId), Utc(5, 15).AddMinutes(30), Utc(6, 15));

        var result = AbsenceEffects.ApplyVacation(vacation, [appointment]);

        AssertError(new AbsenceError.ConflictingAppointments(), result);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Fact]
    public void ApplyVacation_ManuallyCancelledAppointment_PreservesCancellationAndSeriesPosition()
    {
        var cancelled = Ok(SeriesAppointment(80, 1, Utc(5, 15)).Cancel(StaffRole.Superuser, Now));
        var vacation = MakeVacation(81, new VacationSubject.Client(TestData.ClientId), Utc(5, 14), Utc(6, 15));

        var unchanged = Assert.Single(Ok(AbsenceEffects.ApplyVacation(vacation, [cancelled])));

        Assert.Equal(cancelled, unchanged);
        Assert.True(unchanged.CountsTowardSeriesTarget());
        Assert.Null(unchanged.Series!.Compensates);
        Assert.Equal(cancelled, Assert.Single(Ok(AbsenceEffects.ApplyVacation(vacation.Remove(), [unchanged]))));
    }

    [Fact]
    public void ApplyVacation_OtherClient_LeavesAppointmentUnchanged()
    {
        var appointment = MakeAppointment(80, Utc(5, 15));
        var vacation = MakeVacation(81, new VacationSubject.Client(new(Guid(82))), Utc(5, 14), Utc(6, 15));

        Assert.Equal(appointment, Assert.Single(Ok(AbsenceEffects.ApplyVacation(vacation, [appointment]))));
    }

    [Fact]
    public void ApplyClosure_ConflictingAppointment_RequiresExplicitResolution()
    {
        var appointment = MakeAppointment(80, Utc(5, 15));
        var closure = Ok(SchoolClosureRules.Create(new(Guid(83)), Period(Utc(5, 14), Utc(6, 15)), "Ремонт"));

        AssertError(new AbsenceError.ConflictingAppointments(), AbsenceEffects.ApplyClosure(closure, [appointment]));
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Fact]
    public void ApplyVacation_ReplacedTeacher_ResolvesStaffVacationConflict()
    {
        var appointment = Ok(MakeAppointment(80, Utc(5, 15)).ReplaceStaff(StaffRole.Superuser, Now, new(Guid(84))));
        var vacation = MakeVacation(81, new VacationSubject.Staff(TestData.StaffId), Utc(5, 14), Utc(6, 15));

        Assert.Equal(appointment, Assert.Single(Ok(AbsenceEffects.ApplyVacation(vacation, [appointment]))));
    }
}
