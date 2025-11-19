using Facet.Extensions.EFCore;
using FastEndpoints;
using MelodyTrack.Common.Api.Schedule.Requests;
using MelodyTrack.Common.Api.Schedule.Responses;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

public class GetAppointmentsEndpoint(AppDbContext db) : Ep.Req<GetAppointmentsRequest>.Res<Results<Ok<GetAppointmentsResponse>, UnauthorizedHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/appointments");
    }

    public override async Task<Results<Ok<GetAppointmentsResponse>, UnauthorizedHttpResult, ProblemDetails>> ExecuteAsync(GetAppointmentsRequest req, CancellationToken ct)
    {
        var appointments = await db.Appointments
            .Include(e => e.Service)
            .Include(e => e.Client)
            .Where(e => e.StartDate >= DateTime.SpecifyKind(req.StartDate, DateTimeKind.Utc) && e.StartDate <= DateTime.SpecifyKind(req.EndDate, DateTimeKind.Utc))
            .OrderBy(e => e.StartDate)
            .ToFacetsAsync<AppointmentDto>(cancellationToken: ct);

        foreach (var appointment in appointments)
        {
            appointment.StartDate = DateTimeUtils.ConvertDateToTimezone(appointment.StartDate, req.Timezone);
            appointment.EndDate = DateTimeUtils.ConvertDateToTimezone(appointment.EndDate, req.Timezone);
        }

        return TypedResults.Ok(new GetAppointmentsResponse { Appointments = appointments });
    }
}