using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Vacations.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Vacations.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/vacations/{id}/restoration-candidates")]
public sealed class GetVacationRestorationCandidatesEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<Ok<GetVacationRestorationCandidatesResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> HandleAsync(
        Ulid id, AppDbContext db, VacationRestorationService restoration,
        ICurrentUserAccessor currentUserAccessor, HttpContext httpContext, CancellationToken ct)
    {
        var actor = await currentUserAccessor.GetAsync(ct);
        if (actor is null) return TypedResults.Unauthorized();
        var vacation = await db.Vacations.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(item => item.Id == id, ct);
        if (vacation is null) return TypedResults.NotFound(ApiErrorResponseFactory.CreateProblemDetails(httpContext, StatusCodes.Status404NotFound, "Отпуск не найден."));
        if (!VacationService.CanManage(actor, vacation)) return TypedResults.Forbid();
        return TypedResults.Ok(new GetVacationRestorationCandidatesResponse { Items = await restoration.GetCandidatesAsync(id, ct) });
    }
}
