using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.VacationRequests.Requests;
using MelodyTrack.Backend.Api.VacationRequests.Responses;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;

namespace MelodyTrack.Backend.Api.VacationRequests.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/vacation-requests/{id}/approve")]
public sealed class ApproveVacationRequestEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.Superuser)]
    [EnableRateLimiting(ApiRateLimitPolicies.VacationRequests)]
    public static async Task<Results<Ok<VacationRequestResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, Conflict<ApiProblemDetails>>> HandleAsync(
        VacationRequestDecisionRequest request,
        Ulid id,
        ICurrentUserAccessor currentUserAccessor,
        IVacationRequestWorkflowService workflow,
        IVacationRequestQueryService queryService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        request.Id = id;
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await workflow.ApproveAsync(
            id,
            request.ExpectedVersion,
            request.Message,
            request.CancelConflictingAppointments,
            currentUser,
            ct);
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

        var response = await queryService.GetAccessibleAsync(id, currentUser, ct)
            ?? throw new InvalidOperationException("Approved vacation request could not be loaded.");
        return TypedResults.Ok(response);
    }
}
