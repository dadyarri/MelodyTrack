using FastEndpoints;
using MelodyTrack.Backend.Api.ClientPortal.Responses;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ClientPortal.Endpoints;

public class GetClientPortalScheduleEndpoint(AppDbContext db, IRecurringAppointmentMaterializer recurringAppointmentMaterializer)
    : Ep.Req<GetAppointmentsRequest>.Res<Results<Ok<GetClientPortalScheduleResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/client-portal/schedule");
    }

    public override async Task<Results<Ok<GetClientPortalScheduleResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(GetAppointmentsRequest req, CancellationToken ct)
    {
        var currentUser = await EndpointAuthUtils.GetCurrentUserContextAsync(User, db, ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUser.Role.IsClient() || currentUser.LinkedClientId is null)
        {
            return TypedResults.Forbid();
        }

        var clientId = currentUser.LinkedClientId.Value;

        var startUtc = DateTime.SpecifyKind(req.StartDate, DateTimeKind.Utc);
        var endUtc = DateTime.SpecifyKind(req.EndDate, DateTimeKind.Utc);
        await recurringAppointmentMaterializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, ct);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Include(item => item.CourseTheme)
            .Where(item =>
                !item.IsDeleted &&
                item.Client.Id == clientId &&
                item.StartDate >= startUtc &&
                item.StartDate <= endUtc)
            .OrderBy(item => item.StartDate)
            .Take(1)
            .ToListAsync(ct);

        var responseAppointments = appointments
            .Select(ClientPortalAppointmentDto.FromModel)
            .ToList();

        foreach (var appointment in responseAppointments)
        {
            appointment.StartDate = DateTimeUtils.ConvertDateToTimezone(appointment.StartDate, req.Timezone);
            appointment.EndDate = DateTimeUtils.ConvertDateToTimezone(appointment.EndDate, req.Timezone);
        }

        return TypedResults.Ok(new GetClientPortalScheduleResponse
        {
            Appointments = responseAppointments
        });
    }
}
