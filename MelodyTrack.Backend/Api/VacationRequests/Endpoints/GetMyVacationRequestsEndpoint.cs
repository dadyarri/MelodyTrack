using MelodyTrack.Backend.Api.VacationRequests.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.VacationRequests.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/vacation-requests/mine")]
public sealed class GetMyVacationRequestsEndpoint
{
    public static async Task<Results<Ok<GetVacationRequestsResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        ICurrentUserAccessor currentUserAccessor,
        IVacationRequestQueryService queryService,
        CancellationToken ct)
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }
        if (currentUser.Role.RoleName.IsClient())
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok(await queryService.GetMineAsync(currentUser, ct));
    }
}
