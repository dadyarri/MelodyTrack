using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class AbsenceEffectsTests
{
    [Fact]
    public void ApplyVacation_PartialIntersection_CancelsScheduledAppointment()
    {
        var appointment = MakeAppointment(80, Utc(5, 15));
        var vacation = MakeVacation(81, new VacationSubject.Client(TestData.ClientId), Utc(5, 15).AddMinutes(30), Utc(6, 15));
        var cancelled = Assert.Single(AbsenceEffects.ApplyVacation(vacation, [appointment]));
        Assert.Equal(AppointmentStatus.Cancelled, cancelled.Status);
        Assert.Contains(new CancellationReason.Absence(new AbsenceCause.Vacation(vacation.Id)), cancelled.CancellationReasons);
    }
    [Fact]
    public void ApplyVacation_CompletedAppointment_PreservesResult()
    {
        var completed = Ok(MakeAppointment(80, Utc(5, 15)).Complete(StaffRole.Superuser, Utc(5, 16)));
        var vacation = MakeVacation(81, new VacationSubject.Client(TestData.ClientId), Utc(5, 14), Utc(6, 15));
        Assert.Equal(completed, Assert.Single(AbsenceEffects.ApplyVacation(vacation, [completed])));
    }
    [Fact]
    public void ApplyVacation_OtherClient_LeavesAppointmentUnchanged()
    {
        var appointment = MakeAppointment(80, Utc(5, 15));
        var vacation = MakeVacation(81, new VacationSubject.Client(new(Guid(82))), Utc(5, 14), Utc(6, 15));
        Assert.Equal(appointment, Assert.Single(AbsenceEffects.ApplyVacation(vacation, [appointment])));
    }
    [Fact]
    public void ApplyClosure_OverlappingVacation_PreservesBothReasons()
    {
        var vacation = MakeVacation(81, new VacationSubject.Staff(TestData.StaffId), Utc(5, 14), Utc(6, 15));
        var closure = Ok(SchoolClosureRules.Create(new(Guid(83)), Period(Utc(5, 14), Utc(6, 15)), "Ремонт"));
        var cancelled = Assert.Single(AbsenceEffects.ApplyClosure(closure, AbsenceEffects.ApplyVacation(vacation, [MakeAppointment(80, Utc(5, 15))])));
        Assert.Equal(2, cancelled.CancellationReasons.Count);
    }
    [Fact]
    public void ApplyVacation_DeletedVacation_DoesNotAutomaticallyRestoreCancelledAppointment()
    {
        var vacation = MakeVacation(81, new VacationSubject.Client(TestData.ClientId), Utc(5, 14), Utc(6, 15));
        var cancelled = AbsenceEffects.ApplyVacation(vacation, [MakeAppointment(80, Utc(5, 15))]);
        var afterDeletion = AbsenceEffects.ApplyVacation(vacation.Remove(), cancelled);
        Assert.Equal(AppointmentStatus.Cancelled, Assert.Single(afterDeletion).Status);
    }
}
