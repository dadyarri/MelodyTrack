using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Services.RecurringTasks;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using IcalCalendarEvent = Ical.Net.CalendarComponents.CalendarEvent;

namespace MelodyTrack.Backend.Api.CalendarSubscriptions.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/calendar-subscriptions/{token}.ics")]
public sealed class GetCalendarSubscriptionEndpoint
{
    // The upper boundary is inclusive. Past non-cancelled appointments remain
    // in the feed; only future events are limited to this rolling window.
    private const int SubscriptionWindowDays = 14;

        [AllowAnonymous]
    public static async Task<Results<FileContentHttpResult, NotFound>> HandleAsync(
        [AsParameters] CalendarSubscriptionRequest req,
        AppDbContext db,
        IRecurringAppointmentMaterializer recurringAppointmentMaterializer,
        IRecurringTaskService recurringTaskService,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var subscription = await db.CalendarSubscriptions.AsNoTracking().FirstOrDefaultAsync(e => e.Token == req.Token && e.RevokedAtUtc == null, ct);
        if (subscription is null) return TypedResults.NotFound();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var windowEndUtc = now.AddDays(SubscriptionWindowDays);
        if (subscription.UserId is { } subscribedUserId)
        {
            await recurringAppointmentMaterializer.EnsureProviderAppointmentsGeneratedAsync(
                subscribedUserId,
                now,
                windowEndUtc,
                ct);
        }
        else
        {
            await recurringAppointmentMaterializer.EnsureClientAppointmentsGeneratedAsync(
                subscription.ClientId!.Value,
                now,
                windowEndUtc,
                ct);
        }

        var events = subscription.UserId is { } userId
            ? await GetUserEventsAsync(db, recurringTaskService, userId, now, windowEndUtc, ct)
            : await GetClientEventsAsync(db, subscription.ClientId!.Value, windowEndUtc, ct);
        var calendar = BuildCalendar(events, now);
        return TypedResults.File(Encoding.UTF8.GetBytes(calendar), "text/calendar; charset=utf-8", "melodytrack.ics");
    }

    private static async Task<List<CalendarEvent>> GetUserEventsAsync(
        AppDbContext db,
        IRecurringTaskService recurringTaskService,
        Ulid userId,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        CancellationToken ct)
    {
        var taskWindowStartUtc = windowStartUtc.Date;
        var appointments = await db.Appointments.AsNoTracking()
            .Where(e => e.Provider != null && e.Provider.Id == userId && !e.IsDeleted && e.Status != AppointmentStatus.Cancelled
                && e.StartDate <= windowEndUtc)
            .Select(e => new CalendarEvent(e.Id.ToString(), e.StartDate, e.EndDate, $"{e.Service.Name} ({e.Client.LastName} {e.Client.FirstName})", null))
            .ToListAsync(ct);
        var tasks = await recurringTaskService.GetTasksAsync("UTC", null, RecurringTaskListStatus.Open, ct);
        appointments.AddRange(tasks
            .Where(task => task.TeacherId == userId)
            .Select(task =>
            {
                var startAtUtc = task.RelevantAtUtc ?? DateTime.SpecifyKind(task.BusinessDate.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                return new CalendarEvent(
                    $"task-{task.RuleId}-{task.DeduplicationKey}",
                    startAtUtc,
                    startAtUtc.AddMinutes(15),
                    $"{task.Title}: {task.RelatedPersonDisplayName}",
                    null);
            })
            .Where(task => task.StartAtUtc >= taskWindowStartUtc && task.StartAtUtc <= windowEndUtc));
        return appointments;
    }

    private static async Task<List<CalendarEvent>> GetClientEventsAsync(
        AppDbContext db,
        Ulid clientId,
        DateTime windowEndUtc,
        CancellationToken ct)
    {
        return await db.Appointments.AsNoTracking()
            .Where(e => e.Client.Id == clientId && !e.IsDeleted && e.Status != AppointmentStatus.Cancelled
                && e.StartDate <= windowEndUtc)
            .OrderBy(e => e.StartDate)
            .Select(e => new CalendarEvent(e.Id.ToString(), e.StartDate, e.EndDate, e.Service.PublicName ?? e.Service.Name, null))
            .ToListAsync(ct);
    }

    private static string BuildCalendar(IEnumerable<CalendarEvent> events, DateTime generatedAtUtc)
    {
        var calendar = new Calendar
        {
            ProductId = "-//MelodyTrack//Calendar//RU",
            Version = "2.0"
        };
        foreach (var item in events.OrderBy(e => e.StartAtUtc))
        {
            var calendarEvent = new IcalCalendarEvent
            {
                Uid = $"{item.Id}@melodytrack",
                DtStamp = new CalDateTime(generatedAtUtc),
                DtStart = new CalDateTime(item.StartAtUtc),
                DtEnd = new CalDateTime(item.EndAtUtc),
                Summary = item.Summary,
                Description = item.Description
            };
            calendarEvent.Alarms.Add(new Alarm
            {
                Action = "DISPLAY",
                Description = item.Summary,
                Trigger = new Trigger(-Duration.FromMinutes(15))
            });
            calendar.Events.Add(calendarEvent);
        }
        return new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;
    }
    private sealed record CalendarEvent(string Id, DateTime StartAtUtc, DateTime EndAtUtc, string Summary, string? Description);
}

public class CalendarSubscriptionRequest
{
    [FromRoute]
    public required string Token { get; set; }
}
