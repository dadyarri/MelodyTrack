using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.ClientPortal.Requests;
using MelodyTrack.Backend.Api.ClientPortal.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ClientPortal.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/client-portal/schedule")]
public sealed class GetClientPortalScheduleEndpoint
{
    private const int RecurrenceMaterializationHorizonDays = 45;

    [Authorize(Policy = AuthorizationPolicies.ClientPortal)]
    public static async Task<Results<Ok<GetClientPortalScheduleResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        [AsParameters] GetClientPortalScheduleRequest req,
        AppDbContext db,
        IRecurringAppointmentMaterializer recurringAppointmentMaterializer,
        ICurrentUserAccessor currentUserAccessor,
        TimeProvider timeProvider,
        CancellationToken ct
    )
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUser.Role.RoleName.IsClient() || currentUser.ClientId is null)
        {
            return TypedResults.Forbid();
        }

        var clientId = currentUser.ClientId.Value;

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        await recurringAppointmentMaterializer.EnsureClientAppointmentsGeneratedAsync(
            clientId,
            nowUtc,
            nowUtc.AddDays(RecurrenceMaterializationHorizonDays),
            ct);

        var appointment = await db.Appointments
            .AsNoTracking()
            .Include(item => item.CourseTheme)
            .Where(item =>
                !item.IsDeleted &&
                item.Client.Id == clientId &&
                item.Status == AppointmentStatus.Planned &&
                item.EndDate >= nowUtc)
            .OrderBy(item => item.StartDate)
            .Take(1)
            .SingleOrDefaultAsync(ct);

        var responseAppointment = appointment is null ? null : ClientPortalAppointmentDto.FromModel(appointment);
        if (responseAppointment is not null)
        {
            responseAppointment.StartDate = DateTimeUtils.ConvertDateToTimezone(responseAppointment.StartDate, req.Timezone);
            responseAppointment.EndDate = DateTimeUtils.ConvertDateToTimezone(responseAppointment.EndDate, req.Timezone);
        }

        return TypedResults.Ok(new GetClientPortalScheduleResponse
        {
            NextAppointment = responseAppointment
        });
    }
}
