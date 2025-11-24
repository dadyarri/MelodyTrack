using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class LogoutEndpoint(AppDbContext db) : Ep.Req<LogoutRequest>.Res<IResult>
{
    public override void Configure()
    {
        Post("/auth/logout");
    }

    public override async Task<IResult> ExecuteAsync(LogoutRequest req,
        CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (email is null)
        {
            Logger.LogWarning("Logout attempt without valid email claim in token");
            return ApiResults.Unauthorized();
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null)
        {
            Logger.LogWarning("Logout attempt for non-existent user with email {Email}", email.Value);
            return ApiResults.Unauthorized();
        }

        await db.Sessions
            .Where(e => e.RefreshToken == req.RefreshToken)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        Logger.LogInformation("User {Email} successfully logged out", email.Value);
        return ApiResults.NoContent();
    }
}