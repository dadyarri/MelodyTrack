using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Core.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public sealed class SchedulingInterruptionService(
    AppDbContext db,
    IRecurringAppointmentService recurrence,
    IAuditLogService audit,
    TimeProvider timeProvider)
{
    public Task<List<Appointment>> GetVacationConflictsAsync(Vacation vacation, CancellationToken ct) =>
        db.Appointments.Include(item => item.Client).Include(item => item.Service)
            .Include(item => item.RecurringRule).ThenInclude(item => item!.RecurrenceType).Where(item =>
            !item.IsDeleted && item.Status == AppointmentStatus.Planned
            && item.StartDate < vacation.EndDate && item.EndDate > vacation.StartDate
            && (vacation.ClientId != null && item.Client.Id == vacation.ClientId
                || vacation.UserId != null && item.Provider != null && item.Provider.Id == vacation.UserId))
            .ToListAsync(ct);

    public async Task SuspendForVacationAsync(Vacation vacation, IReadOnlyList<Appointment> conflicts, CancellationToken ct)
    {
        var existing = await db.SchedulingInterruptions.Where(item => item.VacationId == vacation.Id && item.RestoredAtUtc == null && item.SupersededAtUtc == null).ToListAsync(ct);
        foreach (var appointment in conflicts)
        {
            appointment.Status = AppointmentStatus.Cancelled;
            if (appointment.RecurringRule is not null && appointment.RecurrenceOrdinal is null)
            {
                appointment.RecurrenceOrdinal = recurrence.GetOrdinal(appointment.RecurringRule, appointment.StartDate);
            }
            AddIfMissing(existing, vacation, appointment, appointment.Id);
            await WriteAppointmentAuditAsync(appointment, AppointmentStatus.Planned, "Отпуск", ct);
        }

        var rules = await db.RecurrenceRules.Include(item => item.Client).Include(item => item.Provider).Include(item => item.Service).Include(item => item.RecurrenceType)
            .Where(item => item.StartDate < vacation.EndDate && (item.EndDate == null || item.EndDate >= vacation.StartDate.AddDays(-1))
                && (vacation.ClientId != null && item.Client.Id == vacation.ClientId
                    || vacation.UserId != null && item.Provider != null && item.Provider.Id == vacation.UserId))
            .ToListAsync(ct);
        var ruleIds = rules.Select(item => item.Id).ToList();
        var appointments = await db.Appointments.Where(item => item.RecurringRuleId != null && ruleIds.Contains(item.RecurringRuleId.Value)
            && item.StartDate < vacation.EndDate && item.EndDate > vacation.StartDate).ToListAsync(ct);
        var interruptedIds = await db.SchedulingInterruptions.Where(item => item.AppointmentId != null && item.RestoredAtUtc == null && item.SupersededAtUtc == null)
            .Select(item => item.AppointmentId!.Value).ToListAsync(ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var rule in rules)
        {
            foreach (var occurrence in recurrence.GetAppointmentsForRule(rule, vacation.StartDate.AddMinutes(-rule.DurationMinutes), vacation.EndDate))
            {
                if (occurrence.StartDate < now || !SchedulingAvailability.Overlaps(occurrence.StartDate, occurrence.EndDate, vacation.StartDate, vacation.EndDate)) continue;
                var stored = appointments.FirstOrDefault(item => item.RecurringRuleId == rule.Id
                    && (item.RecurrenceOrdinal == occurrence.RecurrenceOrdinal || item.StartDate == occurrence.StartDate));
                if (stored is not null && (stored.IsDeleted || stored.Status is AppointmentStatus.Completed or AppointmentStatus.Burned
                    || stored.Status == AppointmentStatus.Cancelled && !interruptedIds.Contains(stored.Id) && !conflicts.Any(item => item.Id == stored.Id))) continue;
                AddIfMissing(existing, vacation, occurrence, stored?.Id);
            }
        }
        await db.SaveChangesAsync(ct);
    }

    private void AddIfMissing(List<SchedulingInterruption> existing, Vacation vacation, Appointment occurrence, Ulid? appointmentId)
    {
        var match = existing.FirstOrDefault(item => appointmentId != null && item.AppointmentId == appointmentId
            || occurrence.RecurringRuleId != null && item.RecurringRuleId == occurrence.RecurringRuleId && item.RecurrenceOrdinal == occurrence.RecurrenceOrdinal);
        if (match is not null)
        {
            match.AppointmentId ??= appointmentId;
            return;
        }
        var interruption = new SchedulingInterruption
        {
            Id = Ulid.NewUlid(),
            VacationId = vacation.Id,
            Vacation = vacation,
            SubjectType = vacation.ClientId != null ? VacationRequestSubjectType.Client : VacationRequestSubjectType.Staff,
            SubjectId = vacation.ClientId ?? vacation.UserId!.Value,
            AppointmentId = appointmentId,
            RecurringRuleId = occurrence.RecurringRuleId,
            RecurrenceOrdinal = occurrence.RecurrenceOrdinal,
            StartDate = occurrence.StartDate,
            EndDate = occurrence.EndDate,
            Reason = "vacation"
        };
        db.SchedulingInterruptions.Add(interruption);
        existing.Add(interruption);
    }

    public Task WriteAppointmentAuditAsync(Appointment appointment, AppointmentStatus previousStatus, string reason, CancellationToken ct) =>
        audit.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.AppointmentUpdated,
            EntityType = "appointment",
            EntityId = appointment.Id.ToString(),
            Details = $"Статус: {previousStatus.ToDisplayName()} → {appointment.Status.ToDisplayName()}; Причина: {reason}"
        }, ct);
}
