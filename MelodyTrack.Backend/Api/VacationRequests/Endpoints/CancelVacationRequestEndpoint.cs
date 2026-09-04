using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.VacationRequests.Requests;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;

namespace MelodyTrack.Backend.Api.VacationRequests.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/vacation-requests/{id}/cancel")]
public sealed class CancelVacationRequestEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    [EnableRateLimiting(ApiRateLimitPolicies.VacationRequests)]
    public static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, Conflict<ApiProblemDetails>>> HandleAsync(
        CancelVacationRequest request,
        Ulid id,
        ICurrentUserAccessor currentUserAccessor,
        IVacationRequestWorkflowService workflow,
        HttpContext httpContext,
        CancellationToken ct)
    {
        request.Id = id;
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await workflow.CancelAsync(id, request.ExpectedVersion, currentUser, ct);
        return result.Failure switch
        {
            VacationRequestWorkflowFailure.None => TypedResults.NoContent(),
            VacationRequestWorkflowFailure.Forbidden => TypedResults.Forbid(),
            VacationRequestWorkflowFailure.NotFound => TypedResults.NotFound(ApiErrorResponseFactory.CreateProblemDetails(
                httpContext,
                StatusCodes.Status404NotFound,
                result.Detail)),
            _ => TypedResults.Conflict(ApiErrorResponseFactory.CreateProblemDetails(
                httpContext,
                StatusCodes.Status409Conflict,
                result.Detail))
        };
    }
}
