using System.Text;
using FastEndpoints;
using Ical.Net;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using IcalCalendarEvent = Ical.Net.CalendarComponents.CalendarEvent;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.CalendarSubscriptions.Endpoints;

public class GetCalendarSubscriptionEndpoint(AppDbContext db) : Ep.Req<CalendarSubscriptionRequest>.Res<Results<FileContentHttpResult, NotFound>>
{
    public override void Configure()
    {
        Get("/calendar-subscriptions/{token}.ics");
        AllowAnonymous();
    }

    public override async Task<Results<FileContentHttpResult, NotFound>> ExecuteAsync(CalendarSubscriptionRequest req, CancellationToken ct)
    {
        var subscription = await db.CalendarSubscriptions.AsNoTracking().FirstOrDefaultAsync(e => e.Token == req.Token && e.RevokedAtUtc == null, ct);
        if (subscription is null) return TypedResults.NotFound();

        var events = subscription.UserId is { } userId
            ? await GetUserEventsAsync(userId, ct)
            : await GetClientEventsAsync(subscription.ClientId!.Value, ct);
        var calendar = BuildCalendar(events);
        return TypedResults.File(Encoding.UTF8.GetBytes(calendar), "text/calendar; charset=utf-8", "melodytrack.ics");
    }

    private async Task<List<CalendarEvent>> GetUserEventsAsync(Ulid userId, CancellationToken ct)
    {
        var appointments = await db.Appointments.AsNoTracking()
            .Where(e => e.Provider != null && e.Provider.Id == userId && !e.IsDeleted && e.Status != AppointmentStatus.Cancelled)
            .Select(e => new CalendarEvent(e.Id.ToString(), e.StartDate, e.EndDate, $"{e.Service.Name} ({e.Client.LastName} {e.Client.FirstName})", null))
            .ToListAsync(ct);
        return appointments;
    }

    private async Task<List<CalendarEvent>> GetClientEventsAsync(Ulid clientId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var history = await db.Appointments.AsNoTracking()
            .Where(e => e.Client.Id == clientId && !e.IsDeleted && e.Status != AppointmentStatus.Cancelled && e.StartDate <= now)
            .Select(e => new CalendarEvent(e.Id.ToString(), e.StartDate, e.EndDate, e.Service.PublicName ?? e.Service.Name, null))
            .ToListAsync(ct);
        var next = await db.Appointments.AsNoTracking()
            .Where(e => e.Client.Id == clientId && !e.IsDeleted && e.Status == AppointmentStatus.Planned && e.StartDate > now)
            .OrderBy(e => e.StartDate)
            .Select(e => new CalendarEvent(e.Id.ToString(), e.StartDate, e.EndDate, e.Service.PublicName ?? e.Service.Name, null))
            .FirstOrDefaultAsync(ct);
        if (next is not null) history.Add(next);
        return history;
    }

    private static string BuildCalendar(IEnumerable<CalendarEvent> events)
    {
        var calendar = new Calendar
        {
            ProductId = "-//MelodyTrack//Calendar//RU",
            Version = "2.0"
        };
        foreach (var item in events.OrderBy(e => e.StartAtUtc))
        {
            calendar.Events.Add(new IcalCalendarEvent
            {
                Uid = $"{item.Id}@melodytrack",
                DtStamp = new CalDateTime(DateTime.UtcNow),
                DtStart = new CalDateTime(item.StartAtUtc),
                DtEnd = new CalDateTime(item.EndAtUtc),
                Summary = item.Summary,
                Description = item.Description
            });
        }
        return new CalendarSerializer().SerializeToString(calendar) ?? string.Empty;
    }
    private sealed record CalendarEvent(string Id, DateTime StartAtUtc, DateTime EndAtUtc, string Summary, string? Description);
}

public class CalendarSubscriptionRequest
{
    public required string Token { get; set; }
}
