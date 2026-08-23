using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UaDetector;
using MelodyTrack.Data.Security;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/auth/login")]
public sealed class LoginEndpoint
{
    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.Login)]
    public static async Task<Results<Ok<LoginAttemptResponse>, Accepted<LoginAttemptResponse>, UnauthorizedHttpResult>> HandleAsync(
        LoginRequest req,
        AppDbContext db,
        [Microsoft.AspNetCore.Mvc.FromServices] IUaDetector uaDetector,
        IAuditLogService auditLogService,
        SessionSecurityMonitor sessionSecurityMonitor,
        RefreshSessionCookieService refreshCookieService,
        CredentialHasher credentialHasher,
        AuthenticationTokenHasher tokenHasher,
        JwtTokenService jwtTokenService,
        TimeProvider timeProvider,
        ILogger<LoginEndpoint> logger,
        HttpContext httpContext,
        CancellationToken ct
    )
    {
        var normalizedEmail = UserUtils.NormalizeEmail(req.Email);
        logger.LogDebug("Attempting to authenticate user {EmailRef}", UserUtils.DescribeEmailForLogs(normalizedEmail));

        var user = await db.Users
            .Include(e => e.Role)
            .WhereEmailMatches(normalizedEmail)
            .FirstOrDefaultAsync(ct);

        if (user is null || !credentialHasher.VerifyPassword(user.Password, req.Password))
        {
            logger.LogWarning("auth.login.failed {EmailRef}", UserUtils.DescribeEmailForLogs(normalizedEmail));
            return TypedResults.Unauthorized();
        }

        var requiresSecondFactor = user.Role.RoleName.IsAnyAdmin() || user.TotpSecret is not null;

        if (requiresSecondFactor && req.Otp is null && string.IsNullOrWhiteSpace(req.RecoveryCode))
        {
            var canUseRecoveryCode = await db.RecoveryCodes
                .AsNoTracking()
                .AnyAsync(e => e.User.Id == user.Id && !e.WasUsed, ct);

            logger.LogInformation("auth.login.challenge_required {EmailRef}", UserUtils.DescribeEmailForLogs(normalizedEmail));
            return TypedResults.Accepted(
                "/auth/login",
                new LoginAttemptResponse
                {
                    RequiresTwoFactor = true,
                    CanUseOtp = user.TotpSecret is not null,
                    CanUseRecoveryCode = canUseRecoveryCode
                });
        }

        if (requiresSecondFactor)
        {
            if (!string.IsNullOrWhiteSpace(req.RecoveryCode))
            {
                var recoveryCode = await db.RecoveryCodes
                    .FirstOrDefaultAsync(e => e.User.Id == user.Id && e.Code == req.RecoveryCode && !e.WasUsed, ct);

                if (recoveryCode is null)
                {
                    logger.LogWarning("auth.login.failed_recovery_code {EmailRef}", UserUtils.DescribeEmailForLogs(normalizedEmail));
                    return TypedResults.Unauthorized();
                }

                recoveryCode.WasUsed = true;
            }
            else if (!UserUtils.VerifyTotpCode(user.TotpSecret!, req.Otp))
            {
                logger.LogWarning("auth.login.failed_otp {EmailRef}", UserUtils.DescribeEmailForLogs(normalizedEmail));
                return TypedResults.Unauthorized();
            }
        }

        var refreshToken = UserUtils.GenerateRandomString(32);
        var deviceInfo = BrowserUtils.GetDeviceInfo(httpContext.Request.Headers, uaDetector);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        await db.Sessions
            .Where(e => e.User.Id == user.Id && !e.WasRevoked && e.ValidUntil >= nowUtc && e.DeviceInfo == deviceInfo)
            .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.WasRevoked, true), ct);

        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = tokenHasher.HashRefreshToken(refreshToken),
            DeviceInfo = deviceInfo,
            ValidUntil = nowUtc.AddDays(7)
        };

        await db.Sessions.AddAsync(session, ct);
        await db.SaveChangesAsync(ct);
        refreshCookieService.Issue(httpContext.Response, refreshToken, session.ValidUntil);
        await sessionSecurityMonitor.AuditFanOutIfUnusualAsync(user, ct);

        logger.LogInformation("auth.login.succeeded {EmailRef} device {DeviceInfo}", UserUtils.DescribeEmailForLogs(user.Email), session.DeviceInfo);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "login_succeeded",
            EntityType = "session",
            EntityId = session.Id.ToString(),
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            ActorDisplayName = $"{user.LastName} {user.FirstName}".Trim(),
            Details = $"Устройство: {session.DeviceInfo}"
        }, ct);
        var response = new LoginAttemptResponse
        {
            AccessToken = jwtTokenService.CreateAccessToken(user, session.Id, timeProvider),
            FirstName = user.FirstName,
            LastName = user.LastName
        };

        return TypedResults.Ok(response);
    }
}
