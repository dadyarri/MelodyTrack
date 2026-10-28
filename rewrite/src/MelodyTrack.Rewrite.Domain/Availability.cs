using System.Collections.Immutable;

namespace MelodyTrack.Rewrite.Domain.Scheduling;

public sealed record BusySlot(AppointmentId AppointmentId, ClientId ClientId, StaffId StaffId, RoomId? RoomId, TimeInterval Period)
{
    public static BusySlot FromAppointment(Appointment appointment) => new(appointment.Id, appointment.ClientId, appointment.StaffId, appointment.RoomId, appointment.Interval);
}

public sealed record AvailabilitySnapshot(ImmutableArray<TimeInterval> WorkingPeriods, ImmutableArray<TimeInterval> Breaks,
    ImmutableArray<Vacation> Vacations, ImmutableArray<SchoolClosure> Closures, ImmutableArray<BusySlot> BusySlots);

public closed record AvailabilityConflict
{
    public sealed record OutsideWorkingHours : AvailabilityConflict;
    public sealed record Break(TimeInterval Period) : AvailabilityConflict;
    public sealed record Vacation(VacationId Id) : AvailabilityConflict;
    public sealed record SchoolClosure(SchoolClosureId Id) : AvailabilityConflict;
    public sealed record ClientBusy(AppointmentId Id) : AvailabilityConflict;
    public sealed record StaffBusy(AppointmentId Id) : AvailabilityConflict;
    public sealed record RoomBusy(AppointmentId Id) : AvailabilityConflict;
}

public static class Availability
{
    internal static bool MatchesSubject(Appointment appointment, VacationSubject subject) => subject switch
    {
        VacationSubject.Client client => client.Id == appointment.ClientId,
        VacationSubject.Staff staff => staff.Id == appointment.StaffId
    };

    public static ImmutableArray<AvailabilityConflict> Check(AvailabilitySnapshot snapshot, Appointment appointment)
    {
        var period = appointment.Interval;
        var conflicts = ImmutableArray.CreateBuilder<AvailabilityConflict>();
        if (!period.IsCoveredBy(snapshot.WorkingPeriods)) conflicts.Add(new AvailabilityConflict.OutsideWorkingHours());
        foreach (var pause in snapshot.Breaks.Where(period.Overlaps)) conflicts.Add(new AvailabilityConflict.Break(pause));
        foreach (var vacation in snapshot.Vacations.Where(value => !value.IsDeleted && MatchesSubject(appointment, value.Subject) && period.Overlaps(value.Period)))
            conflicts.Add(new AvailabilityConflict.Vacation(vacation.Id));
        foreach (var closure in snapshot.Closures.Where(value => !value.IsDeleted && period.Overlaps(value.Period)))
            conflicts.Add(new AvailabilityConflict.SchoolClosure(closure.Id));
        foreach (var busy in snapshot.BusySlots.Where(value => value.AppointmentId != appointment.Id && period.Overlaps(value.Period)))
        {
            if (busy.ClientId == appointment.ClientId) conflicts.Add(new AvailabilityConflict.ClientBusy(busy.AppointmentId));
            if (busy.StaffId == appointment.StaffId) conflicts.Add(new AvailabilityConflict.StaffBusy(busy.AppointmentId));
            if (appointment.RoomId is { } room && room == busy.RoomId) conflicts.Add(new AvailabilityConflict.RoomBusy(busy.AppointmentId));
        }
        return conflicts.ToImmutable();
    }
}

public static class AbsenceEffects
{
    public static ImmutableArray<Appointment> ApplyVacation(Vacation vacation, IEnumerable<Appointment> appointments) => appointments.Select(appointment =>
        !vacation.IsDeleted && Availability.MatchesSubject(appointment, vacation.Subject) && appointment.Interval.Overlaps(vacation.Period)
            ? appointment.CancelForAbsence(new AbsenceCause.Vacation(vacation.Id)) : appointment).ToImmutableArray();

    public static ImmutableArray<Appointment> ApplyClosure(SchoolClosure closure, IEnumerable<Appointment> appointments) => appointments.Select(appointment =>
        !closure.IsDeleted && appointment.Interval.Overlaps(closure.Period)
            ? appointment.CancelForAbsence(new AbsenceCause.SchoolClosure(closure.Id)) : appointment).ToImmutableArray();
}
