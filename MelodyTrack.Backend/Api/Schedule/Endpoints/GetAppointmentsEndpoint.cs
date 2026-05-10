using FastEndpoints;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Api.Schedule.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

public class GetAppointmentsEndpoint(AppDbContext db, IRecurringAppointmentMaterializer recurringAppointmentMaterializer) : Ep.Req<GetAppointmentsRequest>.Res<Results<Ok<GetAppointmentsResponse>, UnauthorizedHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/appointments");
    }

    public override async Task<Results<Ok<GetAppointmentsResponse>, UnauthorizedHttpResult, ProblemDetails>> ExecuteAsync(GetAppointmentsRequest req, CancellationToken ct)
    {
        var startUtc = DateTime.SpecifyKind(req.StartDate, DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(req.EndDate, DateTimeKind.Utc);
        await recurringAppointmentMaterializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, ct);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Include(e => e.Service)
            .Include(e => e.Client)
            .ThenInclude(e => e.Contacts)
            .Include(e => e.Provider)
            .ThenInclude(e => e!.Role)
            .Include(e => e.RecurringRule)
            .ThenInclude(e => e!.RecurrenceType)
            .Where(e => !e.IsDeleted && e.StartDate >= startUtc && e.StartDate <= endUtc)
            .OrderBy(e => e.StartDate)
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

        return TypedResults.Ok(new GetAppointmentsResponse { Appointments = responseAppointments });
    }
}
