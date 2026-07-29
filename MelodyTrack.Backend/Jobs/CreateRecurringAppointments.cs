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
public class CreateRecurringAppointments(
    IRecurringAppointmentMaterializer materializer,
    ILogger<CreateRecurringAppointments> logger,
    TimeProvider timeProvider) : IJob
{
    public static readonly JobKey Key = new("CreateRecurringAppointments");

    public async Task Execute(IJobExecutionContext context)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var scheduledAtUtc = context.ScheduledFireTimeUtc?.UtcDateTime;
        if (scheduledAtUtc.HasValue)
        {
            var delay = now - scheduledAtUtc.Value;
            if (delay > TimeSpan.FromMinutes(1))
            {
                logger.LogWarning("quartz.job.delay {JobKey} {DelayMilliseconds}", context.JobDetail.Key, delay.TotalMilliseconds);
            }
            else
            {
                logger.LogInformation("quartz.job.delay {JobKey} {DelayMilliseconds}", context.JobDetail.Key, Math.Max(0, delay.TotalMilliseconds));
            }
        }

        await materializer.EnsureAppointmentsGeneratedAsync(now, now.AddDays(7), context.CancellationToken);
    }
}
