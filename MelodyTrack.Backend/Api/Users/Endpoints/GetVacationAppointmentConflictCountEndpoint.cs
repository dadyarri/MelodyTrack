using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Users.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Users.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/users/{id}/vacation-appointment-conflict-count")]
public sealed class GetVacationAppointmentConflictCountEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.Superuser)]
    public static async Task<Results<Ok<int>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> HandleAsync(
        [AsParameters] GetVacationAppointmentConflictCountRequest request,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }
        if (!currentUser.Role.RoleName.IsSuperuser())
        {
            return TypedResults.Forbid();
        }

        if (!await db.Users.AsNoTracking().AnyAsync(item => item.Id == request.Id, ct))
        {
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateProblemDetails(
                httpContext,
                StatusCodes.Status404NotFound,
                "Пользователь не найден."));
        }

        var count = await db.Appointments.AsNoTracking().CountAsync(item =>
            !item.IsDeleted &&
            item.Status == AppointmentStatus.Planned &&
            item.Provider != null &&
            item.Provider.Id == request.Id &&
            item.StartDate < request.EndDate &&
            item.EndDate > request.StartDate,
            ct);
        return TypedResults.Ok(count);
    }
}
