using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class Verify2FaEndpoint(AppDbContext db)
    : Ep.Req<Verify2FaRequest>.Res<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/2fa/verify");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        Verify2FaRequest req, CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? req.Email;

        if (email is null)
        {
            Logger.LogWarning("2FA verification attempt without email");
            return TypedResults.Unauthorized();
        }

        var user = await db.Users.FirstOrDefaultAsync(e => e.Email == email, ct);

        if (user is null)
        {
            Logger.LogWarning("2FA verification attempt for non-existent user with email {Email}", email);
            return TypedResults.Unauthorized();
        }

        if (!UserUtils.VerifyTotpCode(req.OtpSecret, req.Otp))
        {
            Logger.LogWarning("Invalid 2FA code provided for user {Email}", email);
            return TypedResults.Unauthorized();
        }

        await db.RecoveryCodes
            .Where(e => e.User.Id == user.Id && !e.WasUsed)
            .ExecuteDeleteAsync(ct);

        var recoveryCodes = UserUtils.GenerateRecoveryCodes().ToList();
        await db.RecoveryCodes.AddRangeAsync(recoveryCodes.Select(code => new RecoveryCode
        {
            Id = Ulid.NewUlid(),
            Code = code,
            User = user
        }), ct);

        user.TotpSecret = req.OtpSecret;
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("Successfully verified and set up 2FA for user {Email}", email);
        return TypedResults.Ok(new RecoveryCodesResponse
        {
            Codes = recoveryCodes,
            AllCodes = recoveryCodes.Select(code => new RecoveryCodeDto
            {
                Code = code,
                WasUsed = false
            }).ToList()
        });
    }
}
