using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class LogoutAllEndpoint(AppDbContext db): Ep.NoReq.Res<Results<ForbidHttpResult, NoContent>>
{
    public override void Configure()
    {
        Post("/auth/logoutAll");
    }

    public override async Task<Results<ForbidHttpResult, NoContent>> ExecuteAsync(CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (email is null)
        {
            return TypedResults.Forbid();
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null)
        {
            return TypedResults.Forbid();
        }

        await db.Sessions
            .Where(e => e.User == user)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        return TypedResults.NoContent();
    }
}