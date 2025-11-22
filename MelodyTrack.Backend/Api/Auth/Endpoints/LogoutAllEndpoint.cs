using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class LogoutAllEndpoint(AppDbContext db) : Ep.NoReq.Res<IResult>
{
    public override void Configure()
    {
        Post("/auth/logoutAll");
    }

    public override async Task<IResult> ExecuteAsync(CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (email is null)
        {
            Logger.LogWarning("Logout all attempt without valid email claim in token");
            return ApiResults.Unauthorized();
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null)
        {
            Logger.LogWarning("Logout all attempt for non-existent user with email {Email}", email.Value);
            return ApiResults.Unauthorized();
        }

        await db.Sessions
            .Where(e => e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        Logger.LogInformation("User {Email} successfully logged out from all sessions", email.Value);
        return ApiResults.NoContent();
    }
}