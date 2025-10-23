using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class LogoutEndpoint(AppDbContext db) : Ep.Req<LogoutRequest>.Res<Results<NoContent, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/logout");
        AllowAnonymous();
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult>> ExecuteAsync(LogoutRequest req,
        CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (email is null)
        {
            return TypedResults.Unauthorized();
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        await db.Sessions
            .Where(e => e.RefreshToken == req.RefreshToken)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        return TypedResults.NoContent();
    }
}