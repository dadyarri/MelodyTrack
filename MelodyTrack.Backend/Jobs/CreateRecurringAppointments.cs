using MelodyTrack.Backend.Services;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Models;
using Microsoft.EntityFrameworkCore;
using Quartz;

namespace MelodyTrack.Backend.Jobs;

/// <summary>
///     Quartz job that creates recurring appointments based on recurrence rules.
///     This job processes all active <see cref="AppointmentRecurrenceRule" /> entries and generates
///     new <see cref="Appointment" /> instances for the current period according to their
///     <see cref="AppointmentRecurrenceRule.RecurrenceType" /> and
///     <see cref="AppointmentRecurrenceRule.RecurrencePattern" />.
/// </summary>
public class CreateRecurringAppointments(AppDbContext db, IRecurringAppointmentService service) : IJob
{
    public static readonly JobKey Key = new("CreateRecurringAppointments");

    public async Task Execute(IJobExecutionContext context)
    {
        // Get all recurrence rules that are still valid
        var now = DateTime.UtcNow;
        var recurrenceRules = await db.RecurrenceRules
            .Include(r => r.Service)
            .Include(r => r.Client)
            .Include(r => r.Provider)
            .Include(r => r.RecurrenceType)
            .Where(r => r.StartDate <= now && (r.EndDate == null || r.EndDate >= now))
            .ToListAsync(context.CancellationToken);

        foreach (var rule in recurrenceRules)
        {
            var appointmentsToCreate = service.GetAppointmentsForRule(rule, now).ToList();

            await db.Appointments.AddRangeAsync(appointmentsToCreate, context.CancellationToken);
        }

        await db.SaveChangesAsync(context.CancellationToken);
    }
}