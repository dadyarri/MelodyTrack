using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class Remove2FaEndpoint(AppDbContext db)
    : Ep.NoReq.Res<IResult>
{
    public override void Configure()
    {
        Delete("/auth/2fa/delete");
    }

    public override async Task<IResult> ExecuteAsync(
        CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (email is null)
        {
            Logger.LogWarning("2FA removal attempt without valid email claim in token");
            return ApiResults.Unauthorized();
        }

        var user = await db.Users
            .Include(user => user.Role)
            .FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null)
        {
            Logger.LogWarning("2FA removal attempt for non-existent user with email {Email}", email.Value);
            return ApiResults.Unauthorized();
        }

        if (user.Role.RoleName.IsAnyAdmin())
        {
            Logger.LogWarning("Attempt to remove 2FA for admin user {Email} - operation not allowed", email.Value);
            return ApiResults.Forbid("Администраторам запрещено отключать 2FA");
        }

        user.TotpSecret = null;
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("Successfully removed 2FA for user {Email}", email.Value);
        return ApiResults.NoContent();
    }
}