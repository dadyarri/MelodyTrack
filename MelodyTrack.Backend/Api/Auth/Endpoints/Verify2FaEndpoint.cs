using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/auth/2fa/verify")]
public sealed class Verify2FaEndpoint
{

        [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.VerifyTwoFactor)]
    public static async Task<Results<Ok<RecoveryCodesResponse>, UnauthorizedHttpResult>> HandleAsync(
        Verify2FaRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        ILogger<Verify2FaEndpoint> logger,
        CancellationToken ct
    )
    {
        var authenticatedEmail = currentUserAccessor.Email;
        var email = authenticatedEmail ?? req.Email;

        if (email is null)
        {
            logger.LogWarning("2FA verification attempt without email");
            return TypedResults.Unauthorized();
        }

        if (authenticatedEmail is not null
            && req.Email is not null
            && !string.Equals(UserUtils.NormalizeEmail(authenticatedEmail), UserUtils.NormalizeEmail(req.Email), StringComparison.Ordinal))
        {
            logger.LogWarning(
                "Authenticated 2FA verification attempt with mismatched email claim {AuthenticatedEmail} and payload {PayloadEmail}",
                authenticatedEmail,
                req.Email);
            return TypedResults.Unauthorized();
        }

        var normalizedEmail = UserUtils.NormalizeEmail(email);
        var user = await db.Users.WhereEmailMatches(normalizedEmail).FirstOrDefaultAsync(ct);

        if (user is null)
        {
            logger.LogWarning("2FA verification attempt for non-existent {EmailRef}", UserUtils.DescribeEmailForLogs(normalizedEmail));
            return TypedResults.Unauthorized();
        }

        if (authenticatedEmail is null && user.TotpSecret != req.OtpSecret)
        {
            logger.LogWarning("Anonymous 2FA verification attempt with mismatched secret for {EmailRef}", UserUtils.DescribeEmailForLogs(normalizedEmail));
            return TypedResults.Unauthorized();
        }

        if (!UserUtils.VerifyTotpCode(req.OtpSecret, req.Otp))
        {
            logger.LogWarning("Invalid 2FA code provided for {EmailRef}", UserUtils.DescribeEmailForLogs(normalizedEmail));
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

        logger.LogInformation("auth.2fa.enrolled {EmailRef}", UserUtils.DescribeEmailForLogs(normalizedEmail));
        return TypedResults.Ok(new RecoveryCodesResponse
        {
            AllCodes = recoveryCodes.Select(code => new RecoveryCodeDto
            {
                Code = code,
                WasUsed = false
            }).ToList()
        });
    }
}
