using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.VacationRequests.Requests;
using MelodyTrack.Backend.Api.WorkingHoursRequests.Responses;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.WorkingHoursRequests.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/working-hours-requests")]
public sealed class GetWorkingHoursRequestReviewQueueEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.Superuser)]
    public static async Task<Results<Ok<GetWorkingHoursRequestsResponse>, ValidationProblem>> HandleAsync(
        [AsParameters] GetVacationRequestsRequest request,
        IWorkingHoursRequestQueryService queryService,
        CancellationToken ct)
    {
        if (request.View is not ("pending" or "history"))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["view"] = ["Допустимые значения: pending или history."]
            });
        }

        return TypedResults.Ok(await queryService.GetForReviewAsync(request.View == "pending", ct));
    }
}
