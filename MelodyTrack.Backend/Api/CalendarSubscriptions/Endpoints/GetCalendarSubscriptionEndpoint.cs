using System.Text;
using FastEndpoints;
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

public class GetCalendarSubscriptionEndpoint(
    AppDbContext db,
    IRecurringAppointmentMaterializer recurringAppointmentMaterializer,
    IRecurringTaskService recurringTaskService,
    TimeProvider timeProvider)
    : Ep.Req<CalendarSubscriptionRequest>.Res<Results<FileContentHttpResult, NotFound>>
{
    private const int ClientMaterializationHorizonDays = 14;
    private const int UserMaterializationHorizonDays = 31;

    public override void Configure()
    {
        Get("/calendar-subscriptions/{token}.ics");
        AllowAnonymous();
        Description(builder => builder.Produces(StatusCodes.Status200OK, contentType: "text/calendar"));
    }

    public override async Task<Results<FileContentHttpResult, NotFound>> ExecuteAsync(CalendarSubscriptionRequest req, CancellationToken ct)
    {
        var subscription = await db.CalendarSubscriptions.AsNoTracking().FirstOrDefaultAsync(e => e.Token == req.Token && e.RevokedAtUtc == null, ct);
        if (subscription is null) return TypedResults.NotFound();

        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (subscription.UserId is { } subscribedUserId)
        {
            await recurringAppointmentMaterializer.EnsureProviderAppointmentsGeneratedAsync(
                subscribedUserId,
                now,
                now.AddDays(UserMaterializationHorizonDays),
                ct);
        }
        else
        {
            await recurringAppointmentMaterializer.EnsureClientAppointmentsGeneratedAsync(
                subscription.ClientId!.Value,
                now,
                now.AddDays(ClientMaterializationHorizonDays),
                ct);
        }

        var events = subscription.UserId is { } userId
            ? await GetUserEventsAsync(userId, ct)
            : await GetClientEventsAsync(subscription.ClientId!.Value, ct);
        var calendar = BuildCalendar(events, now);
        return TypedResults.File(Encoding.UTF8.GetBytes(calendar), "text/calendar; charset=utf-8", "melodytrack.ics");
    }

    private async Task<List<CalendarEvent>> GetUserEventsAsync(Ulid userId, CancellationToken ct)
    {
        var appointments = await db.Appointments.AsNoTracking()
            .Where(e => e.Provider != null && e.Provider.Id == userId && !e.IsDeleted && e.Status != AppointmentStatus.Cancelled)
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
            }));
        return appointments;
    }

    private async Task<List<CalendarEvent>> GetClientEventsAsync(Ulid clientId, CancellationToken ct)
    {
        return await db.Appointments.AsNoTracking()
            .Where(e => e.Client.Id == clientId && !e.IsDeleted && e.Status != AppointmentStatus.Cancelled)
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
    public required string Token { get; set; }
}
