using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.VacationRequests.Requests;
using MelodyTrack.Backend.Api.VacationRequests.Responses;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;

namespace MelodyTrack.Backend.Api.VacationRequests.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/client-portal/vacation-requests")]
public sealed class CreateClientVacationRequestEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.ClientPortal)]
    [EnableRateLimiting(ApiRateLimitPolicies.VacationRequests)]
    public static async Task<Results<Created<VacationRequestResponse>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<ApiProblemDetails>>> HandleAsync(
        CreateVacationRequest request,
        ICurrentUserAccessor currentUserAccessor,
        IVacationRequestWorkflowService workflow,
        IVacationRequestQueryService queryService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await workflow.CreateClientRequestAsync(currentUser, request, ct);
        if (result.Failure == VacationRequestWorkflowFailure.Forbidden)
        {
            return TypedResults.Forbid();
        }
        if (result.Failure == VacationRequestWorkflowFailure.Conflict)
        {
            return TypedResults.Conflict(ApiErrorResponseFactory.CreateProblemDetails(
                httpContext,
                StatusCodes.Status409Conflict,
                result.Detail));
        }

        var response = await queryService.GetAccessibleAsync(result.Request!.Id, currentUser, ct)
            ?? throw new InvalidOperationException("Created vacation request could not be loaded.");
        return TypedResults.Created($"/client-portal/vacation-requests/{response.Id}", response);
    }
}
