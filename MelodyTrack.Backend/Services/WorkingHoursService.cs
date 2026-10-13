using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Core.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public sealed class WorkingHoursService(AppDbContext db, SchedulingInterruptionService interruptions, IAuditLogService audit, TimeProvider timeProvider)
{
    public async Task<string?> ApplyAsync(User user, IReadOnlyList<UserWorkingHoursDaySnapshot> days, string timezone,
        bool cancelConflicts, CancellationToken ct)
    {
        await VacationRequestSubjectLock.AcquireSchedulingAsync(db, ct);
        var current = user.WorkingHours.Count == 0 ? UserAvailabilityService.GetDefaultWorkingHours()
            : user.WorkingHours.Select(item => new UserWorkingHoursDaySnapshot(item.DayOfWeek, item.IsWorkingDay, item.StartMinuteOfDay, item.EndMinuteOfDay)).ToList();
        if (user.ScheduleTimezone == timezone && current.OrderBy(item => item.DayOfWeek).SequenceEqual(days.OrderBy(item => item.DayOfWeek))) return null;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var planned = await db.Appointments.Where(item => !item.IsDeleted && item.Status == AppointmentStatus.Planned
            && item.Provider != null && item.Provider.Id == user.Id && item.StartDate > now).ToListAsync(ct);
        var hours = days.Select(item => new SchedulingWorkingDay(item.DayOfWeek, item.IsWorkingDay, item.StartMinuteOfDay, item.EndMinuteOfDay)).ToList();
        var conflicting = planned.Where(item => SchedulingAvailability.Evaluate(item.StartDate, item.EndDate, [], [], hours, timezone).Count > 0).ToList();
        if (conflicting.Count > 0 && !cancelConflicts)
            return $"Новый график конфликтует с будущими занятиями: {conflicting.Count}. Разрешите отмену или сначала измените расписание.";
        db.UserWorkingHoursDays.RemoveRange(user.WorkingHours);
        user.WorkingHours = days.Select(day => new UserWorkingHoursDay
        {
            Id = Ulid.NewUlid(), UserId = user.Id, User = user,
            DayOfWeek = day.DayOfWeek, IsWorkingDay = day.IsWorkingDay,
            StartMinuteOfDay = day.StartMinuteOfDay, EndMinuteOfDay = day.EndMinuteOfDay
        }).ToList();
        user.ScheduleTimezone = timezone;
        foreach (var appointment in conflicting)
        {
            appointment.Status = AppointmentStatus.Cancelled;
            db.SchedulingInterruptions.Add(new SchedulingInterruption
            {
                Id = Ulid.NewUlid(), AppointmentId = appointment.Id, Appointment = appointment,
                RecurringRuleId = appointment.RecurringRuleId, RecurrenceOrdinal = appointment.RecurrenceOrdinal,
                SubjectType = VacationRequestSubjectType.Staff, SubjectId = user.Id,
                StartDate = appointment.StartDate, EndDate = appointment.EndDate, Reason = "working_hours"
            });
        }
        await db.SaveChangesAsync(ct);
        foreach (var appointment in conflicting)
            await interruptions.WriteAppointmentAuditAsync(appointment, AppointmentStatus.Planned, "Изменение рабочего графика", ct);
        await audit.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.WorkingHoursChanged,
            EntityType = "user_availability", EntityId = user.Id.ToString(),
            Details = $"Рабочий график изменён; Часовой пояс: {timezone}; Отменено будущих занятий: {conflicting.Count}"
        }, ct);
        return null;
    }
}
