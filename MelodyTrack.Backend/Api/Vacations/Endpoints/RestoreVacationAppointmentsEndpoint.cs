using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Vacations.Requests;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Vacations.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/vacations/{id}/restore-appointments")]
public sealed class RestoreVacationAppointmentsEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, Conflict<ApiProblemDetails>>> HandleAsync(
        Ulid id, RestoreVacationAppointmentsInput input, VacationRestorationService restoration,
        ICurrentUserAccessor currentUserAccessor, HttpContext httpContext, CancellationToken ct)
    {
        var actor = await currentUserAccessor.GetAsync(ct);
        if (actor is null) return TypedResults.Unauthorized();
        var result = await restoration.RestoreAsync(id, input.ExpectedVersion, input.InterruptionIds, actor, ct);
        if (result.Failure == VacationRequestWorkflowFailure.Forbidden) return TypedResults.Forbid();
        if (result.Failure == VacationRequestWorkflowFailure.NotFound)
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateProblemDetails(httpContext, StatusCodes.Status404NotFound, result.Detail));
        if (result.Failure == VacationRequestWorkflowFailure.Conflict)
            return TypedResults.Conflict(ApiErrorResponseFactory.CreateProblemDetails(httpContext, StatusCodes.Status409Conflict, result.Detail));
        return TypedResults.NoContent();
    }
}
