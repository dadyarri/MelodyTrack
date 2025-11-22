using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Common.Api.Auth.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Models;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class RecoveryCodesEndpoint(AppDbContext db)
    : Ep.NoReq.Res<IResult>
{
    public override void Configure()
    {
        Post("/auth/recoveryCodes");
    }

    public override async Task<IResult> ExecuteAsync(
        CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (email is null)
        {
            Logger.LogWarning("Recovery codes generation attempt without valid email claim in token");
            return ApiResults.Unauthorized();
        }

        Logger.LogDebug("Attempting to generate recovery codes for user {Email}", email.Value);
        var user = await db.Users.FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null)
        {
            Logger.LogWarning("Recovery codes generation attempt for non-existent user with email {Email}", email.Value);
            return ApiResults.Unauthorized();
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
        return ApiResults.Ok(new RecoveryCodesResponse
        {
            Codes = recoveryCodes
        });
    }
}