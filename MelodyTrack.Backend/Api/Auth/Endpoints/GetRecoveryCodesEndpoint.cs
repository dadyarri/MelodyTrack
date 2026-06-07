using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class GetRecoveryCodesEndpoint(AppDbContext db)
    : Ep.NoReq.Res<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/auth/recoveryCodes");
    }

    public override async Task<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

        if (email is null)
        {
            Logger.LogWarning("Recovery codes list request without valid email claim in token");
            return TypedResults.Unauthorized();
        }

        var user = await db.Users.FirstOrDefaultAsync(e => e.Email == email, ct);

        if (user is null)
        {
            Logger.LogWarning("Recovery codes list request for non-existent user with email {Email}", email);
            return TypedResults.Unauthorized();
        }

        var codes = await db.RecoveryCodes
            .Where(e => e.User.Id == user.Id)
            .Select(e => new RecoveryCodeDto
            {
                Code = e.Code,
                WasUsed = e.WasUsed
            })
            .OrderBy(e => e.WasUsed)
            .ThenBy(e => e.Code)
            .ToListAsync(ct);

        return TypedResults.Ok(new RecoveryCodesResponse
        {
            AllCodes = codes
        });
    }
}
