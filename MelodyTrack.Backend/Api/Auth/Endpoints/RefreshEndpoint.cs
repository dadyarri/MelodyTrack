using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UaDetector;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class RefreshEndpoint(
    AppDbContext db,
    IUaDetector uaDetector,
    IAuditLogService auditLogService,
    SessionSecurityMonitor sessionSecurityMonitor,
    RefreshSessionCookieService refreshCookieService,
    TimeProvider timeProvider)
    : Ep.Req<RefreshRequest>.Res<Results<Ok<LoginResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/refresh");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting(ApiRateLimitPolicies.Refresh));
        Description(builder => builder.Produces<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, ApiMediaTypes.ProblemJson));
    }

    public override async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(RefreshRequest req,
        CancellationToken ct)
    {
        Logger.LogDebug("Attempting to refresh token");
        var cookieRefreshToken = refreshCookieService.ReadRefreshToken(HttpContext.Request);
        var isLegacyMigration = !string.IsNullOrWhiteSpace(req.RefreshToken);
        var presentedRefreshToken = isLegacyMigration ? req.RefreshToken : cookieRefreshToken;
        if (string.IsNullOrWhiteSpace(presentedRefreshToken))
        {
            return TypedResults.Unauthorized();
        }

        if (!isLegacyMigration && !refreshCookieService.HasValidCsrfToken(HttpContext.Request, presentedRefreshToken))
        {
            Logger.LogWarning("auth.refresh.csrf_rejected");
            return TypedResults.Forbid();
        }

        var refreshTokenHash = UserUtils.HashOpaqueToken(presentedRefreshToken);

        var session = await db.Sessions
            .Where(e => e.RefreshToken == refreshTokenHash)
            .Include(e => e.User)
            .FirstOrDefaultAsync(ct);
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;


        if (session is null)
        {
            Logger.LogWarning("Unknown refresh token used in refresh attempt");
            return TypedResults.Unauthorized();
        }

        if (session.WasRevoked)
        {
            Logger.LogWarning(
                "Revoked refresh token replay detected for {EmailRef}. Revoking all sessions.",
                UserUtils.DescribeEmailForLogs(session.User.Email));
            await db.Sessions
                .Where(e => e.User.Id == session.User.Id && !e.WasRevoked)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);
            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Category = "security",
                Action = "refresh_replay_detected",
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
            Logger.LogWarning("Expired refresh token used in refresh attempt for {EmailRef}", UserUtils.DescribeEmailForLogs(session.User.Email));
            session.WasRevoked = true;
            await db.SaveChangesAsync(ct);

            return TypedResults.Unauthorized();
        }

        await db.Sessions.Where(e => e.RefreshToken == refreshTokenHash)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        var refreshToken = UserUtils.GenerateRandomString(32);
        var deviceInfo = BrowserUtils.GetDeviceInfo(HttpContext.Request.Headers, uaDetector);

        await db.Sessions
            .Where(e => e.User.Id == session.User.Id && !e.WasRevoked && e.ValidUntil >= nowUtc && e.DeviceInfo == deviceInfo)
            .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.WasRevoked, true), ct);

        var newSession = new Session
        {
            Id = Ulid.NewUlid(),
            User = session.User,
            RefreshToken = UserUtils.HashOpaqueToken(refreshToken),
            DeviceInfo = deviceInfo,
            ValidUntil = nowUtc.AddDays(7)
        };

        await db.Sessions.AddAsync(newSession, ct);
        await db.SaveChangesAsync(ct);
        refreshCookieService.Issue(HttpContext.Response, refreshToken, newSession.ValidUntil);
        await sessionSecurityMonitor.AuditFanOutIfUnusualAsync(session.User, ct);

        Logger.LogInformation(
            "auth.refresh.succeeded {EmailRef} device {DeviceInfo} legacyMigration {LegacyMigration}",
            UserUtils.DescribeEmailForLogs(session.User.Email),
            newSession.DeviceInfo,
            isLegacyMigration);
        var response = new LoginResponse
        {
            AccessToken = UserUtils.CreateAccessToken(session.User, newSession.Id, timeProvider),
            FirstName = session.User.FirstName,
            LastName = session.User.LastName
        };

        return TypedResults.Ok(response);
    }
}
