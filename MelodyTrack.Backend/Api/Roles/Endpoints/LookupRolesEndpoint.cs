using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Roles.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Roles.Endpoints;

public class LookupRolesEndpoint(AppDbContext db)
    : Ep.NoReq.Res<Results<Ok<LookupRolesResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/roles/lookup");
    }

    public override async Task<Results<Ok<LookupRolesResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (login is null)
        {
            Logger.LogWarning("Role lookup request without valid email claim in token");
            return TypedResults.Unauthorized();
        }

        var user = await db.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .WhereEmailMatches(login.Value)
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            Logger.LogWarning("Role lookup request for non-existent {EmailRef}", UserUtils.DescribeEmailForLogs(login.Value));
            return TypedResults.Unauthorized();
        }

        if (!user.Role.RoleName.IsAnyAdmin())
        {
            Logger.LogWarning("Role lookup request denied for non-admin {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
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

        Logger.LogInformation("Returned {Count} assignable roles to {EmailRef}", roles.Count, UserUtils.DescribeEmailForLogs(user.Email));
        return TypedResults.Ok(new LookupRolesResponse { Roles = roles });
    }
}
