using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public interface IRecurringAppointmentMaterializer
{
    Task EnsureAppointmentsGeneratedAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct);
}

public class RecurringAppointmentMaterializer(AppDbContext db, IRecurringAppointmentService recurringAppointmentService)
    : IRecurringAppointmentMaterializer
{
    private const int AdvisoryLockKey = 41051;

    public async Task EnsureAppointmentsGeneratedAsync(DateTime startUtc, DateTime endUtc, CancellationToken ct)
    {
        if (endUtc < startUtc)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", ct);

        var recurrenceRules = await db.RecurrenceRules
            .Include(rule => rule.Service)
            .Include(rule => rule.Client)
            .ThenInclude(client => client.Vacations)
            .Include(rule => rule.Provider)
            .Include(rule => rule.RecurrenceType)
            .Where(rule => rule.StartDate <= endUtc && (rule.EndDate == null || rule.EndDate >= startUtc))
            .ToListAsync(ct);

        if (recurrenceRules.Count == 0)
        {
            await transaction.CommitAsync(ct);
            return;
        }

        var ruleIds = recurrenceRules.Select(rule => rule.Id).ToList();
        var existingAppointments = await db.Appointments
            .Where(appointment =>
                appointment.RecurringRule != null &&
                ruleIds.Contains(appointment.RecurringRule.Id) &&
                appointment.StartDate >= startUtc &&
                appointment.StartDate <= endUtc)
            .Select(appointment => new
            {
                RuleId = appointment.RecurringRule!.Id,
                appointment.StartDate
            })
            .ToListAsync(ct);

        var existingKeys = existingAppointments
            .Select(item => GetKey(item.RuleId, item.StartDate))
            .ToHashSet(StringComparer.Ordinal);

        var appointmentsToCreate = new List<Appointment>();

        foreach (var recurrenceRule in recurrenceRules)
        {
            foreach (var appointment in recurringAppointmentService.GetAppointmentsForRule(recurrenceRule, startUtc, endUtc))
            {
                var appointmentDate = DateOnly.FromDateTime(appointment.StartDate);
                if (recurrenceRule.Client.Vacations.Any(vacation => vacation.StartDate <= appointmentDate && vacation.EndDate >= appointmentDate))
                {
                    continue;
                }

                var key = GetKey(recurrenceRule.Id, appointment.StartDate);
                if (!existingKeys.Add(key))
                {
                    continue;
                }

                appointmentsToCreate.Add(appointment);
            }
        }

        if (appointmentsToCreate.Count > 0)
        {
            await db.Appointments.AddRangeAsync(appointmentsToCreate, ct);
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    private static string GetKey(Ulid ruleId, DateTime startDate)
    {
        return $"{ruleId}:{startDate.Ticks}";
    }
}
