using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class Remove2FaEndpoint(AppDbContext db)
    : Ep.NoReq.Res<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Delete("/auth/2fa/delete");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (email is null)
        {
            Logger.LogWarning("2FA removal attempt without valid email claim in token");
            return TypedResults.Unauthorized();
        }

        var user = await db.Users
            .Include(user => user.Role)
            .FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null)
        {
            Logger.LogWarning("2FA removal attempt for non-existent user with email {Email}", email.Value);
            return TypedResults.Unauthorized();
        }

        if (user.Role.RoleName.IsAnyAdmin())
        {
            Logger.LogWarning("Attempt to remove 2FA for admin user {Email} - operation not allowed", email.Value);
            return TypedResults.Forbid();
        }

        user.TotpSecret = null;
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("auth.2fa.removed user {Email}", email.Value);
        return TypedResults.NoContent();
    }
}
