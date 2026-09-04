using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.VacationRequests.Requests;
using MelodyTrack.Backend.Api.WorkingHoursRequests.Responses;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;

namespace MelodyTrack.Backend.Api.WorkingHoursRequests.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/working-hours-requests/{id}/approve")]
public sealed class ApproveWorkingHoursRequestEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.Superuser)]
    [EnableRateLimiting(ApiRateLimitPolicies.VacationRequests)]
    public static async Task<Results<Ok<WorkingHoursRequestResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, Conflict<ApiProblemDetails>>> HandleAsync(
        VacationRequestDecisionRequest request,
        Ulid id,
        ICurrentUserAccessor currentUserAccessor,
        IWorkingHoursRequestWorkflowService workflow,
        IWorkingHoursRequestQueryService queryService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        request.Id = id;
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await workflow.ApproveAsync(id, request.ExpectedVersion, request.Message, currentUser, ct);
        if (result.Failure == VacationRequestWorkflowFailure.Forbidden)
        {
            return TypedResults.Forbid();
        }
        if (result.Failure == VacationRequestWorkflowFailure.NotFound)
        {
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateProblemDetails(httpContext, StatusCodes.Status404NotFound, result.Detail));
        }
        if (result.Failure == VacationRequestWorkflowFailure.Conflict)
        {
            return TypedResults.Conflict(ApiErrorResponseFactory.CreateProblemDetails(httpContext, StatusCodes.Status409Conflict, result.Detail));
        }

        return TypedResults.Ok((await queryService.GetAccessibleAsync(id, currentUser, ct))!);
    }
}
