using MelodyTrack.Backend.Api.WorkingHoursRequests.Requests;
using MelodyTrack.Backend.Api.WorkingHoursRequests.Responses;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;

namespace MelodyTrack.Backend.Api.WorkingHoursRequests.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/working-hours-requests")]
public sealed class CreateWorkingHoursRequestEndpoint
{
    [EnableRateLimiting(ApiRateLimitPolicies.VacationRequests)]
    public static async Task<Results<Created<WorkingHoursRequestResponse>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<ApiProblemDetails>>> HandleAsync(
        CreateWorkingHoursRequest request,
        ICurrentUserAccessor currentUserAccessor,
        IWorkingHoursRequestWorkflowService workflow,
        IWorkingHoursRequestQueryService queryService,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        var result = await workflow.CreateAsync(currentUser, request, ct);
        if (result.Failure == VacationRequestWorkflowFailure.Forbidden)
        {
            return TypedResults.Forbid();
        }
        if (result.Failure == VacationRequestWorkflowFailure.Conflict)
        {
            return TypedResults.Conflict(ApiErrorResponseFactory.CreateProblemDetails(httpContext, StatusCodes.Status409Conflict, result.Detail));
        }

        var response = await queryService.GetAccessibleAsync(result.Request!.Id, currentUser, ct)
            ?? throw new InvalidOperationException("Created working hours request could not be loaded.");
        return TypedResults.Created($"/working-hours-requests/{response.Id}", response);
    }
}
