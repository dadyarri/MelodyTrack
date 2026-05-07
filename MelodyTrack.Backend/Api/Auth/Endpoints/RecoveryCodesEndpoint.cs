using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class RecoveryCodesEndpoint(AppDbContext db)
    : Ep.NoReq.Res<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/recoveryCodes");
    }

    public override async Task<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (email is null)
        {
            Logger.LogWarning("Recovery codes generation attempt without valid email claim in token");
            return TypedResults.Unauthorized();
        }

        Logger.LogDebug("Attempting to generate recovery codes for user {Email}", email.Value);
        var user = await db.Users.FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null)
        {
            Logger.LogWarning("Recovery codes generation attempt for non-existent user with email {Email}", email.Value);
            return TypedResults.Unauthorized();
        }

        Logger.LogDebug("Invalidating existing unused recovery codes for user {Email}", email.Value);
        await db.RecoveryCodes
            .Where(e => !e.WasUsed && e.User == user)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasUsed, true), ct);

        var recoveryCodes = UserUtils.GenerateRecoveryCodes().ToList();

        foreach (var recoveryCode in recoveryCodes)
        {
            var code = new RecoveryCode
            {
                Id = Ulid.NewUlid(),
                Code = recoveryCode,
                User = user
            };

            await db.RecoveryCodes.AddAsync(code, ct);
        }

        await db.SaveChangesAsync(ct);

        Logger.LogInformation("Successfully generated {Count} new recovery codes for user {Email}", recoveryCodes.Count, email.Value);
        return TypedResults.Ok(new RecoveryCodesResponse
        {
            Codes = recoveryCodes
        });
    }
}