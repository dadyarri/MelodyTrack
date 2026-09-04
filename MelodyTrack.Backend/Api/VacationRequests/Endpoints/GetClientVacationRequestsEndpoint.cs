using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.VacationRequests.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.VacationRequests.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/client-portal/vacation-requests")]
public sealed class GetClientVacationRequestsEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.ClientPortal)]
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
        if (!currentUser.Role.RoleName.IsClient() || currentUser.ClientId is null)
        {
            return TypedResults.Forbid();
        }

        return TypedResults.Ok(await queryService.GetMineAsync(currentUser, ct));
    }
}
