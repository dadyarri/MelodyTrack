using MelodyTrack.Rewrite.Domain.Scheduling;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class AvailabilityTests
{
    private static SchoolClosure Closure() => Ok(SchoolClosure.Create(new(Guid(73)), Period(Utc(5, 14), Utc(5, 17)), "Закрыто"));
    [Fact]
    public void Check_ExactWorkingWindowWithoutConflicts_ReturnsNoReasons()
    {
        var appointment = MakeAppointment(70, Utc(5, 15));
        Assert.Empty(Availability.Check(Available(appointment), appointment));
    }
    [Fact]
    public void Check_PartialWorkingCoverage_RejectsWholeAppointment()
    {
        var appointment = MakeAppointment(70, Utc(5, 15));
        var snapshot = Available(appointment) with { WorkingPeriods = [Period(Utc(5, 15), Utc(5, 16).AddTicks(-1))] };
        Assert.Contains(new AvailabilityConflict.OutsideWorkingHours(), Availability.Check(snapshot, appointment));
    }
    [Fact]
    public void Check_BreakInsideAppointment_ReportsBreak()
    {
        var appointment = MakeAppointment(70, Utc(5, 15));
        var pause = Period(Utc(5, 15).AddMinutes(30), Utc(5, 15).AddMinutes(45));
        Assert.Contains(new AvailabilityConflict.Break(pause), Availability.Check(Available(appointment) with { Breaks = [pause] }, appointment));
    }
    [Fact]
    public void Check_ClientVacation_DoesNotBlockAnotherClient()
    {
        var appointment = MakeAppointment(70, Utc(5, 15));
        var vacation = MakeVacation(72, new VacationSubject.Client(new(Guid(71))), Utc(5, 14), Utc(5, 17));
        Assert.Empty(Availability.Check(Available(appointment) with { Vacations = [vacation] }, appointment));
    }
    [Fact]
    public void Check_OwnVacationAndClosure_ReportsBothCauses()
    {
        var appointment = MakeAppointment(70, Utc(5, 15));
        var vacation = MakeVacation(72, new VacationSubject.Staff(TestData.StaffId), Utc(5, 14), Utc(5, 17));
        var closure = Closure();
        var reasons = Availability.Check(Available(appointment) with { Vacations = [vacation], Closures = [closure] }, appointment);
        Assert.Contains(new AvailabilityConflict.Vacation(vacation.Id), reasons);
        Assert.Contains(new AvailabilityConflict.SchoolClosure(closure.Id), reasons);
    }
    [Fact]
    public void Check_DeletedAbsences_DoNotBlockAppointment()
    {
        var appointment = MakeAppointment(70, Utc(5, 15));
        var vacation = MakeVacation(72, new VacationSubject.Staff(TestData.StaffId), Utc(5, 14), Utc(5, 17)).Remove();
        Assert.Empty(Availability.Check(Available(appointment) with { Vacations = [vacation], Closures = [Closure().Remove()] }, appointment));
    }
    [Fact]
    public void Check_SameParticipantsAndRoom_ReportsAllResourceConflicts()
    {
        var appointment = MakeAppointment(70, Utc(5, 15));
        var busy = BusySlot.FromAppointment(MakeAppointment(71, Utc(5, 15)));
        var reasons = Availability.Check(Available(appointment) with { BusySlots = [busy] }, appointment);
        Assert.Contains(new AvailabilityConflict.ClientBusy(busy.AppointmentId), reasons);
        Assert.Contains(new AvailabilityConflict.StaffBusy(busy.AppointmentId), reasons);
        Assert.Contains(new AvailabilityConflict.RoomBusy(busy.AppointmentId), reasons);
    }
    [Fact]
    public void Check_NoAssignedRoom_DoesNotInventRoomConflict()
    {
        var appointment = Ok(Appointment.Create(new(Guid(70)), TestData.ClientId, TestData.ServiceId, TestData.StaffId, null, Utc(5, 15), Duration(60)));
        var busy = BusySlot.FromAppointment(MakeAppointment(71, Utc(5, 15)));
        Assert.DoesNotContain(new AvailabilityConflict.RoomBusy(busy.AppointmentId), Availability.Check(Available(appointment) with { BusySlots = [busy] }, appointment));
    }
    [Fact]
    public void Check_AdjacentAppointment_DoesNotConflict()
    {
        var appointment = MakeAppointment(70, Utc(5, 15));
        var busy = BusySlot.FromAppointment(MakeAppointment(71, Utc(5, 16)));
        Assert.Empty(Availability.Check(Available(appointment) with { BusySlots = [busy] }, appointment));
    }
    [Fact]
    public void Check_CurrentAppointment_DoesNotConflictWithItself()
    {
        var appointment = MakeAppointment(70, Utc(5, 15));
        Assert.Empty(Availability.Check(Available(appointment) with { BusySlots = [BusySlot.FromAppointment(appointment)] }, appointment));
    }
}
