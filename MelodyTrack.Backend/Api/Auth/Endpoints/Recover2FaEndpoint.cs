using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UaDetector;
using MelodyTrack.Data.Security;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/auth/2fa/recover")]
public sealed class Recover2FaEndpoint
{

        [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.RecoverTwoFactor)]
    public static async Task<Results<Ok<Recover2FaResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        Recover2FaRequest req,
        AppDbContext db,
        [Microsoft.AspNetCore.Mvc.FromServices] IUaDetector uaDetector,
        RefreshSessionCookieService refreshCookieService,
        AuthenticationTokenHasher tokenHasher,
        JwtTokenService jwtTokenService,
        TimeProvider timeProvider,
        ILogger<Recover2FaEndpoint> logger,
        HttpContext httpContext,
        CancellationToken ct
    )
    {
        var normalizedEmail = UserUtils.NormalizeEmail(req.Email);
        var user = await db.Users
            .WhereEmailMatches(normalizedEmail)
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            logger.LogWarning("2FA recovery attempt for non-existent {EmailRef}", UserUtils.DescribeEmailForLogs(normalizedEmail));
            return TypedResults.Unauthorized();
        }

        var recoveryCode = await db.RecoveryCodes
            .Where(e => e.User.Id == user.Id && e.Code == req.RecoveryCode && !e.WasUsed)
            .FirstOrDefaultAsync(ct);

        if (recoveryCode is null)
        {
            logger.LogWarning("2FA recovery attempt with invalid or used recovery code for {EmailRef}", UserUtils.DescribeEmailForLogs(normalizedEmail));
            return TypedResults.Forbid();
        }

        await db.Sessions
            .Where(e => e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        await db.RecoveryCodes
            .Where(e => e.User.Id == user.Id && !e.WasUsed && e.Id != recoveryCode.Id)
            .ExecuteDeleteAsync(ct);

        var refreshToken = UserUtils.GenerateRandomString(32);

        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = tokenHasher.HashRefreshToken(refreshToken),
            DeviceInfo = BrowserUtils.GetDeviceInfo(httpContext.Request.Headers, uaDetector),
            ValidUntil = timeProvider.GetUtcNow().UtcDateTime.AddDays(7)
        };

        var (secret, otpUrl) = UserUtils.GenerateTotp(user.Email);
        var recoveryCodes = UserUtils.GenerateRecoveryCodes().ToList();

        var response = new Recover2FaResponse
        {
            AccessToken = jwtTokenService.CreateAccessToken(user, session.Id, timeProvider),
            Secret = secret,
            OtpUrl = otpUrl,
            AllCodes = recoveryCodes.Select(code => new RecoveryCodeDto
            {
                Code = code,
                WasUsed = false
            }).ToList()
        };

        recoveryCode.WasUsed = true;
        user.TotpSecret = secret;
        await db.RecoveryCodes.AddRangeAsync(recoveryCodes.Select(code => new RecoveryCode
        {
            Id = Ulid.NewUlid(),
            Code = code,
            User = user
        }), ct);
        await db.Sessions.AddAsync(session, ct);
        await db.SaveChangesAsync(ct);
        refreshCookieService.Issue(httpContext.Response, refreshToken, session.ValidUntil);

        logger.LogInformation(
            "Successfully recovered 2FA for {EmailRef}. New session created from {DeviceInfo}",
            UserUtils.DescribeEmailForLogs(user.Email),
            session.DeviceInfo
        );
        return TypedResults.Ok(response);
    }
}
