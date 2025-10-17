using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class Revome2FaEndpoint(AppDbContext db)
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
            return TypedResults.Unauthorized();
        }

        var user = await db.Users
            .Include(user => user.Role)
            .FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        if (user.Role.RoleName.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        user.TotpSecret = null;
        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }
}