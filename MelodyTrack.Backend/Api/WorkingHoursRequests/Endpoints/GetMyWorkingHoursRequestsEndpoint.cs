using MelodyTrack.Backend.Api.WorkingHoursRequests.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.WorkingHoursRequests.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/working-hours-requests/mine")]
public sealed class GetMyWorkingHoursRequestsEndpoint
{
    public static async Task<Results<Ok<GetWorkingHoursRequestsResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        ICurrentUserAccessor currentUserAccessor,
        IWorkingHoursRequestQueryService queryService,
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
