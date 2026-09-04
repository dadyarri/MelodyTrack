using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Roles.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Roles.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/roles/options")]
public sealed class LookupRolesEndpoint
{

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<Ok<LookupRolesResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<LookupRolesEndpoint> logger,
        CancellationToken ct
    )
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            logger.LogWarning("Role lookup request without a current user");
            return TypedResults.Unauthorized();
        }

        if (!user.Role.RoleName.IsAnyAdmin())
        {
            logger.LogWarning("Role lookup request denied for non-admin {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
            return TypedResults.Forbid();
        }

        var roles = await db.Roles
            .AsNoTracking()
            .Where(role =>
                role.RoleName != UserRoles.Client &&
                (user.Role.RoleName.IsSuperuser() || role.RoleName != UserRoles.Superuser))
            .OrderBy(e => e.DisplayName)
            .Select(e => new LookupRolesDto
            {
                Id = e.Id,
                DisplayName = e.DisplayName
            })
            .ToListAsync(ct);

        logger.LogInformation("Returned {Count} assignable roles to {EmailRef}", roles.Count, UserUtils.DescribeEmailForLogs(user.Email));
        return TypedResults.Ok(new LookupRolesResponse { Roles = roles });
    }
}
