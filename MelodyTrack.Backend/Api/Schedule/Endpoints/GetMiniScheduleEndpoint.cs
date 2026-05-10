using System.Globalization;
using FastEndpoints;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Api.Schedule.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

public class GetMiniScheduleEndpoint(AppDbContext db, IRecurringAppointmentMaterializer recurringAppointmentMaterializer) : Ep.Req<BaseGetAppointmentsRequest>.Res<Results<Ok<GetMiniScheduleResponse>, UnauthorizedHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/appointments/mini");
    }

    public override async Task<Results<Ok<GetMiniScheduleResponse>, UnauthorizedHttpResult, ProblemDetails>> ExecuteAsync(BaseGetAppointmentsRequest req, CancellationToken ct)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(req.Timezone);
        var today = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timezone).Date;
        var startUtc = TimeZoneInfo.ConvertTimeToUtc(today, timezone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(today.AddDays(2), timezone);
        await recurringAppointmentMaterializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, ct);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Where(e => !e.IsDeleted && e.StartDate >= startUtc && e.StartDate < endUtc)
            .Include(e => e.Client)
            .ThenInclude(e => e.Contacts)
            .Include(e => e.Service)
            .Include(e => e.Provider)
            .ThenInclude(e => e!.Role)
            .Include(e => e.RecurringRule)
            .ThenInclude(e => e!.RecurrenceType)
            .OrderBy(e => e.StartDate)
            .ThenBy(e => e.Client.LastName)
            .ThenBy(e => e.Client.FirstName)
            .ToListAsync(ct);

        var responseAppointments = appointments
            .Select(AppointmentDto.FromModel)
            .ToList();

        foreach (var appointment in responseAppointments)
        {
            appointment.StartDate = DateTimeUtils.ConvertDateToTimezone(appointment.StartDate, req.Timezone);
            appointment.EndDate = DateTimeUtils.ConvertDateToTimezone(appointment.EndDate, req.Timezone);
            if (appointment.RecurringRule is not null)
            {
                appointment.RecurringRule.StartDate = DateTimeUtils.ConvertDateToTimezone(appointment.RecurringRule.StartDate, req.Timezone);
                appointment.RecurringRule.EndDate = appointment.RecurringRule.EndDate is { } endDate
                    ? DateTimeUtils.ConvertDateToTimezone(endDate, req.Timezone)
                    : null;
            }
        }

        var appointmentsDays = responseAppointments
            .GroupBy(e => e.StartDate.Date)
            .OrderBy(e => e.Key)
            .ToDictionary(
                e => e.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                e => e.ToList()
            );

        var result = new GetMiniScheduleResponse
        {
            Appointments = appointmentsDays
        };

        return TypedResults.Ok(result);
    }
}
