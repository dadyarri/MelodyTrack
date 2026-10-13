using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Core.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public interface IRecurringAppointmentMaterializer
{
    Task EnsureAppointmentsGeneratedAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct);
    Task EnsureClientAppointmentsGeneratedAsync(Ulid clientId, DateTime startUtc, DateTime endUtc, CancellationToken ct);
    Task EnsureProviderAppointmentsGeneratedAsync(Ulid providerId, DateTime startUtc, DateTime endUtc, CancellationToken ct);
}

public class RecurringAppointmentMaterializer(AppDbContext db, IRecurringAppointmentService recurringAppointmentService)
    : IRecurringAppointmentMaterializer
{

    public Task EnsureAppointmentsGeneratedAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        return EnsureAppointmentsGeneratedAsync(startUtc, endUtc, null, null, ct);
    }

    public Task EnsureClientAppointmentsGeneratedAsync(Ulid clientId, DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        return EnsureAppointmentsGeneratedAsync(startUtc, endUtc, clientId, null, ct);
    }

    public Task EnsureProviderAppointmentsGeneratedAsync(Ulid providerId, DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        return EnsureAppointmentsGeneratedAsync(startUtc, endUtc, null, providerId, ct);
    }

    private async Task EnsureAppointmentsGeneratedAsync(
        DateTime startUtc,
        DateTime endUtc,
        Ulid? clientId,
        Ulid? providerId,
        CancellationToken ct)
    {
        if (endUtc < startUtc)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await VacationRequestSubjectLock.AcquireSchedulingAsync(db, ct);

        var recurrenceRulesQuery = db.RecurrenceRules
            .Include(rule => rule.Service)
            .Include(rule => rule.Client)
            .ThenInclude(client => client.Vacations)
            .Include(rule => rule.Provider)
            .ThenInclude(provider => provider!.Vacations)
            .Include(rule => rule.Provider).ThenInclude(provider => provider!.WorkingHours)
            .Include(rule => rule.RecurrenceType)
            .Where(rule => rule.StartDate <= endUtc && (rule.EndDate == null || rule.EndDate >= startUtc));

        if (clientId is not null)
        {
            recurrenceRulesQuery = recurrenceRulesQuery.Where(rule => rule.Client.Id == clientId);
        }

        if (providerId is not null)
        {
            recurrenceRulesQuery = recurrenceRulesQuery.Where(rule => rule.Provider != null && rule.Provider.Id == providerId);
        }

        var recurrenceRules = await recurrenceRulesQuery.ToListAsync(ct);

        if (recurrenceRules.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return;
        }

        var ruleIds = recurrenceRules.Select(rule => rule.Id).ToList();
        var existingAppointments = await db.Appointments.AsNoTracking()
            .Where(item => item.RecurringRuleId != null && ruleIds.Contains(item.RecurringRuleId.Value))
            .Select(item => new { RuleId = item.RecurringRuleId!.Value, item.RecurrenceOrdinal, item.StartDate }).ToListAsync(ct);
        var existingKeys = existingAppointments.Where(item => item.RecurrenceOrdinal != null)
            .Select(item => (item.RuleId, item.RecurrenceOrdinal!.Value)).ToHashSet();
        var existingDates = existingAppointments.Select(item => (item.RuleId, item.StartDate)).ToHashSet();
        var interruptions = await db.SchedulingInterruptions.AsNoTracking().Where(item => item.RecurringRuleId != null
            && ruleIds.Contains(item.RecurringRuleId.Value) && item.RestoredAtUtc == null).ToListAsync(ct);
        var interruptedKeys = interruptions.Where(item => item.RecurrenceOrdinal != null)
            .Select(item => (item.RecurringRuleId!.Value, item.RecurrenceOrdinal!.Value)).ToHashSet();
        var busy = await db.Appointments.AsNoTracking().Include(item => item.Client).Include(item => item.Provider)
            .Where(item => !item.IsDeleted && item.Status != AppointmentStatus.Cancelled
                && item.StartDate <= endUtc && item.EndDate > startUtc).ToListAsync(ct);
        var appointmentsToCreate = new List<Appointment>();
        var occurrences = recurrenceRules.SelectMany(rule => recurringAppointmentService.GetAppointmentsForRule(rule, startUtc, endUtc))
            .OrderBy(item => item.StartDate).ThenBy(item => item.RecurringRuleId).ToList();
        foreach (var appointment in occurrences)
        {
            var key = (appointment.RecurringRuleId!.Value, appointment.RecurrenceOrdinal!.Value);
            if (existingKeys.Contains(key) || existingDates.Contains((key.Item1, appointment.StartDate)) || interruptedKeys.Contains(key)) continue;
            var blockers = SchedulingAvailabilityService.EvaluateLoaded(appointment).ToList();
            if (busy.Concat(appointmentsToCreate).Any(item => (item.Client.Id == appointment.Client.Id
                || appointment.Provider != null && item.Provider?.Id == appointment.Provider.Id)
                && SchedulingAvailability.Overlaps(appointment.StartDate, appointment.EndDate, item.StartDate, item.EndDate)))
                blockers.Add(new SchedulingBlocker("participant_busy", "Участник занят другим занятием."));
            if (blockers.Count > 0)
            {
                foreach (var blocker in blockers)
                {
                    db.SchedulingInterruptions.Add(new SchedulingInterruption
                    {
                        Id = Ulid.NewUlid(), VacationId = blocker.VacationId,
                        SubjectType = blocker.Code == "client_vacation" || appointment.Provider is null ? VacationRequestSubjectType.Client : VacationRequestSubjectType.Staff,
                        SubjectId = blocker.Code == "client_vacation" || appointment.Provider is null ? appointment.Client.Id : appointment.Provider.Id,
                        RecurringRuleId = appointment.RecurringRuleId, RecurrenceOrdinal = appointment.RecurrenceOrdinal,
                        StartDate = appointment.StartDate, EndDate = appointment.EndDate, Reason = blocker.Code
                    });
                }
                interruptedKeys.Add(key);
                continue;
            }
            existingKeys.Add(key);
            appointmentsToCreate.Add(appointment);
        }

        if (appointmentsToCreate.Count > 0)
        {
            await db.Appointments.AddRangeAsync(appointmentsToCreate, ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

}
