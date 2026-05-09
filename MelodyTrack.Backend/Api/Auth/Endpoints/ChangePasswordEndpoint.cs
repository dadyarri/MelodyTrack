using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class ChangePasswordEndpoint(AppDbContext db)
    : Ep.Req<ChangePasswordRequest>.Res<Results<NoContent, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/changePassword");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult>> ExecuteAsync(ChangePasswordRequest req, CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

        if (email is null)
        {
            Logger.LogWarning("Password change request without valid email claim in token");
            return TypedResults.Unauthorized();
        }

        var user = await db.Users.FirstOrDefaultAsync(e => e.Email == email, ct);

        if (user is null || !UserUtils.IsValidPassword(user.Password, req.CurrentPassword))
        {
            Logger.LogWarning("Password change failed for user {Email}: invalid current password", email);
            return TypedResults.Unauthorized();
        }

        UserUtils.HashPassword(req.NewPassword, out var hash);
        user.Password = hash;
        await db.SaveChangesAsync(ct);

        await db.Sessions
            .Where(e => e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        Logger.LogInformation("Password changed for user {Email}; all sessions were revoked", email);
        return TypedResults.NoContent();
    }
}
