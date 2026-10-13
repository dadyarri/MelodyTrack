using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Core.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public interface ISchedulingAvailabilityService
{
    Task<IReadOnlyList<SchedulingBlocker>> EvaluateAsync(Ulid clientId, Ulid? providerId, DateTime startUtc, DateTime endUtc, IReadOnlyCollection<Ulid>? excludedAppointmentIds, CancellationToken ct, IReadOnlyCollection<Ulid>? excludedRuleIds = null);
}

public sealed class SchedulingAvailabilityService(AppDbContext db, IUserAvailabilityService userAvailabilityService, IRecurringAppointmentService recurrence) : ISchedulingAvailabilityService
{
    public async Task<IReadOnlyList<SchedulingBlocker>> EvaluateAsync(
        Ulid clientId,
        Ulid? providerId,
        DateTime startUtc,
        DateTime endUtc,
        IReadOnlyCollection<Ulid>? excludedAppointmentIds,
        CancellationToken ct, IReadOnlyCollection<Ulid>? excludedRuleIds = null)
    {
        var clientVacations = await db.Vacations.AsNoTracking()
            .Where(item => item.ClientId == clientId && item.StartDate < endUtc && item.EndDate > startUtc)
            .Select(item => new SchedulingVacation(item.Id, item.StartDate, item.EndDate))
            .ToListAsync(ct);
        var availability = providerId is null ? null : await userAvailabilityService.GetAvailabilityAsync(providerId.Value, ct);
        var occupied = db.Appointments.AsNoTracking().Where(item =>
            !item.IsDeleted && item.Status != AppointmentStatus.Cancelled
            && item.StartDate < endUtc && item.EndDate > startUtc
            && (item.Client.Id == clientId || providerId != null && item.Provider != null && item.Provider.Id == providerId));
        if (excludedAppointmentIds is { Count: > 0 })
        {
            occupied = occupied.Where(item => !excludedAppointmentIds.Contains(item.Id));
        }
        var appointments = await occupied.Select(item => new
        {
            item.Id,
            ClientId = item.Client.Id,
            ProviderId = item.Provider == null ? (Ulid?)null : item.Provider.Id,
            item.StartDate,
            item.EndDate
        }).ToListAsync(ct);

        var rulesQuery = db.RecurrenceRules.AsNoTracking().Include(item => item.Client).ThenInclude(item => item.Vacations)
            .Include(item => item.Provider).ThenInclude(item => item!.Vacations)
            .Include(item => item.Provider).ThenInclude(item => item!.WorkingHours)
            .Include(item => item.Service).Include(item => item.RecurrenceType)
            .Where(item => item.StartDate < endUtc && (item.EndDate == null || item.EndDate >= startUtc.AddDays(-1))
                && (item.Client.Id == clientId || providerId != null && item.Provider != null && item.Provider.Id == providerId));
        if (excludedRuleIds is { Count: > 0 }) rulesQuery = rulesQuery.Where(item => !excludedRuleIds.Contains(item.Id));
        var rules = await rulesQuery.ToListAsync(ct);
        var ruleIds = rules.Select(item => item.Id).ToList();
        var instances = await db.Appointments.AsNoTracking().Where(item => item.RecurringRuleId != null && ruleIds.Contains(item.RecurringRuleId.Value))
            .Select(item => new { item.RecurringRuleId, item.RecurrenceOrdinal, item.StartDate }).ToListAsync(ct);
        var suppressed = await db.SchedulingInterruptions.AsNoTracking().Where(item => item.RecurringRuleId != null
            && ruleIds.Contains(item.RecurringRuleId.Value) && item.RestoredAtUtc == null)
            .Select(item => new { item.RecurringRuleId, item.RecurrenceOrdinal }).ToListAsync(ct);
        var virtualAppointments = rules.SelectMany(rule => recurrence.GetAppointmentsForRule(rule, startUtc.AddMinutes(-rule.DurationMinutes), endUtc))
            .Where(item => SchedulingAvailability.Overlaps(startUtc, endUtc, item.StartDate, item.EndDate)
                && !instances.Any(stored => stored.RecurringRuleId == item.RecurringRuleId
                    && (stored.RecurrenceOrdinal == item.RecurrenceOrdinal || stored.StartDate == item.StartDate))
                && !suppressed.Any(stored => stored.RecurringRuleId == item.RecurringRuleId && stored.RecurrenceOrdinal == item.RecurrenceOrdinal)
                && EvaluateLoaded(item).Count == 0)
            .Select(item => new { item.Id, ClientId = item.Client.Id, ProviderId = item.Provider?.Id, item.StartDate, item.EndDate });
        appointments.AddRange(virtualAppointments);

        return SchedulingAvailability.Evaluate(
            startUtc,
            endUtc,
            clientVacations,
            availability?.Vacations.Select(item => new SchedulingVacation(item.Id, item.StartDate, item.EndDate)).ToList() ?? [],
            availability?.WorkingHours.Select(item => new SchedulingWorkingDay(item.DayOfWeek, item.IsWorkingDay, item.StartMinuteOfDay, item.EndMinuteOfDay)).ToList(),
            availability?.Timezone ?? "UTC",
            appointments.Where(item => item.ClientId == clientId).Select(item => new SchedulingBusyInterval(item.Id, item.StartDate, item.EndDate)).ToList(),
            appointments.Where(item => providerId != null && item.ProviderId == providerId).Select(item => new SchedulingBusyInterval(item.Id, item.StartDate, item.EndDate)).ToList());
    }

    public static IReadOnlyList<SchedulingBlocker> EvaluateLoaded(Appointment appointment)
    {
        var provider = appointment.Provider;
        var workingHours = provider is null ? null : provider.WorkingHours.Count == 0
            ? UserAvailabilityService.GetDefaultWorkingHours().Select(item => new SchedulingWorkingDay(item.DayOfWeek, item.IsWorkingDay, item.StartMinuteOfDay, item.EndMinuteOfDay)).ToList()
            : provider.WorkingHours.Select(item => new SchedulingWorkingDay(item.DayOfWeek, item.IsWorkingDay, item.StartMinuteOfDay, item.EndMinuteOfDay)).ToList();
        return SchedulingAvailability.Evaluate(
            appointment.StartDate,
            appointment.EndDate,
            appointment.Client.Vacations.Where(item => !item.IsCancelled).Select(item => new SchedulingVacation(item.Id, item.StartDate, item.EndDate)).ToList(),
            provider?.Vacations.Where(item => !item.IsCancelled).Select(item => new SchedulingVacation(item.Id, item.StartDate, item.EndDate)).ToList() ?? [],
            workingHours,
            provider?.ScheduleTimezone ?? "UTC");
    }
}
