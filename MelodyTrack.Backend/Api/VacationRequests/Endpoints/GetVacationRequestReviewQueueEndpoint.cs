using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.VacationRequests.Requests;
using MelodyTrack.Backend.Api.VacationRequests.Responses;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.VacationRequests.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/vacation-requests")]
public sealed class GetVacationRequestReviewQueueEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.Superuser)]
    public static async Task<Results<Ok<GetVacationRequestsResponse>, ValidationProblem>> HandleAsync(
        [AsParameters] GetVacationRequestsRequest request,
        IVacationRequestQueryService queryService,
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
