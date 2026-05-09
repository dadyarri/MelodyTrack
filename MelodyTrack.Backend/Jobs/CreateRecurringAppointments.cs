using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Quartz;

namespace MelodyTrack.Backend.Jobs;

/// <summary>
///     Quartz job that creates recurring appointments based on recurrence rules.
///     This job processes all active <see cref="AppointmentRecurrenceRule" /> entries and generates
///     new <see cref="Appointment" /> instances for the current period according to their
///     <see cref="AppointmentRecurrenceRule.RecurrenceType" /> and
///     <see cref="AppointmentRecurrenceRule.RecurrencePattern" />.
/// </summary>
public class CreateRecurringAppointments(IRecurringAppointmentMaterializer materializer) : IJob
{
    public static readonly JobKey Key = new("CreateRecurringAppointments");

    public async Task Execute(IJobExecutionContext context)
    {
        var now = DateTime.UtcNow;
        await materializer.EnsureAppointmentsGeneratedAsync(now, now.AddDays(7), context.CancellationToken);
    }
}
