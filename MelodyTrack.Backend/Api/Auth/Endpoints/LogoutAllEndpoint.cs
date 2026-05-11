using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class LogoutAllEndpoint(AppDbContext db) : Ep.NoReq.Res<Results<UnauthorizedHttpResult, NoContent>>
{
    public override void Configure()
    {
        Post("/auth/logoutAll");
    }

    public override async Task<Results<UnauthorizedHttpResult, NoContent>> ExecuteAsync(CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (email is null)
        {
            Logger.LogWarning("Logout all attempt without valid email claim in token");
            return TypedResults.Unauthorized();
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null)
        {
            Logger.LogWarning("Logout all attempt for non-existent user with email {Email}", email.Value);
            return TypedResults.Unauthorized();
        }

        await db.Sessions
            .Where(e => e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        Logger.LogInformation("auth.logout_all.succeeded user {Email}", email.Value);
        return TypedResults.NoContent();
    }
}
