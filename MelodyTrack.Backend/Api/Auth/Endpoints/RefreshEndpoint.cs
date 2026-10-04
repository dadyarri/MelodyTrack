using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Data.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using UaDetector;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/auth/refresh")]
public sealed class RefreshEndpoint
{

    [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.Refresh)]
    public static async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
    AppDbContext db,
    [Microsoft.AspNetCore.Mvc.FromServices] IUaDetector uaDetector,
    IAuditLogService auditLogService,
    SessionSecurityMonitor sessionSecurityMonitor,
    RefreshSessionCookieService refreshCookieService,
    AuthenticationTokenHasher tokenHasher,
    JwtTokenService jwtTokenService,
    TimeProvider timeProvider,
    ILogger<RefreshEndpoint> logger,
    HttpContext httpContext,
    CancellationToken ct
)
    {
        logger.LogDebug("Attempting to refresh token");
        var presentedRefreshToken = refreshCookieService.ReadRefreshToken(httpContext.Request);
        if (string.IsNullOrWhiteSpace(presentedRefreshToken))
        {
            return TypedResults.Unauthorized();
        }

        if (!refreshCookieService.HasValidCsrfToken(httpContext.Request, presentedRefreshToken))
        {
            logger.LogWarning("auth.refresh.csrf_rejected");
            return TypedResults.Forbid();
        }

        var refreshTokenHash = tokenHasher.HashRefreshToken(presentedRefreshToken);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var session = await db.Sessions
            .Where(e => e.RefreshToken == refreshTokenHash)
            .Include(e => e.User)
                .ThenInclude(e => e.Role)
            .FirstOrDefaultAsync(ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;

        if (session is null)
        {
            logger.LogWarning("Unknown refresh token used in refresh attempt");
            return TypedResults.Unauthorized();
        }

        if (session.User.Role.RoleName.IsClient())
        {
            // Serialize portal refresh with link entry, rotation, and revocation.
            await db.ClientPortalLoginLinks
                .FromSqlInterpolated($"SELECT * FROM \"ClientPortalLoginLinks\" WHERE \"UserId\" = {session.User.Id.ToByteArray()} FOR UPDATE")
                .ToListAsync(ct);
            await db.Entry(session).ReloadAsync(ct);
        }

        if (session.WasRevoked)
        {
            logger.LogWarning(
                "Revoked refresh token replay detected for {EmailRef}. Revoking all sessions.",
                UserUtils.DescribeEmailForLogs(session.User.Email));
            await db.Sessions
                .Where(e => e.User.Id == session.User.Id && !e.WasRevoked)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);
            await transaction.CommitAsync(ct);
            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.RefreshReplayDetected,
                EntityType = "session",
                EntityId = session.Id.ToString(),
                ActorUserId = session.User.Id,
                ActorEmail = session.User.Email,
                ActorDisplayName = $"{session.User.LastName} {session.User.FirstName}".Trim(),
                Details = "Повторно использован отозванный токен обновления; все сессии завершены"
            }, ct);

            return TypedResults.Unauthorized();
        }

        if (session.ValidUntil < nowUtc)
        {
            logger.LogWarning("Expired refresh token used in refresh attempt for {EmailRef}", UserUtils.DescribeEmailForLogs(session.User.Email));
            session.WasRevoked = true;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return TypedResults.Unauthorized();
        }

        await db.Sessions.Where(e => e.RefreshToken == refreshTokenHash)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        var refreshToken = UserUtils.GenerateRandomString(32);
        var deviceInfo = BrowserUtils.GetDeviceInfo(httpContext.Request.Headers, uaDetector);

        var newSession = new Session
        {
            Id = Ulid.NewUlid(),
            User = session.User,
            RefreshToken = tokenHasher.HashRefreshToken(refreshToken),
            DeviceInfo = deviceInfo,
            ValidUntil = nowUtc.AddDays(session.User.Role.RoleName.IsClient() ? 30 : 7)
        };

        await db.Sessions.AddAsync(newSession, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        refreshCookieService.Issue(httpContext.Response, refreshToken, newSession.ValidUntil);
        await sessionSecurityMonitor.AuditFanOutIfUnusualAsync(session.User, ct);

        logger.LogInformation(
            "auth.refresh.succeeded {EmailRef} device {DeviceInfo}",
            UserUtils.DescribeEmailForLogs(session.User.Email),
            newSession.DeviceInfo);
        var response = new LoginResponse
        {
            AccessToken = jwtTokenService.CreateAccessToken(session.User, newSession.Id, timeProvider),
            FirstName = session.User.FirstName,
            LastName = session.User.LastName
        };

        return TypedResults.Ok(response);
    }
}
