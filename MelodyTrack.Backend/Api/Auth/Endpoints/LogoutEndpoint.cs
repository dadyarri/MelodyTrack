using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using MelodyTrack.Data.Security;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/auth/logout")]
public sealed class LogoutEndpoint
{
    [AllowAnonymous]
    public static async Task<Results<NoContent, ForbidHttpResult>> HandleAsync(
        AppDbContext db,
        IAuditLogService auditLogService,
        RefreshSessionCookieService refreshCookieService,
        AuthenticationTokenHasher tokenHasher,
        ILogger<LogoutEndpoint> logger,
        HttpContext httpContext,
        CancellationToken ct
    )
    {
        var refreshToken = refreshCookieService.ReadRefreshToken(httpContext.Request);
        if (refreshToken is null)
        {
            refreshCookieService.Clear(httpContext.Response);
            return TypedResults.NoContent();
        }

        if (!refreshCookieService.HasValidCsrfToken(httpContext.Request, refreshToken))
        {
            logger.LogWarning("auth.logout.csrf_rejected");
            return TypedResults.Forbid();
        }

        var refreshTokenHash = tokenHasher.HashRefreshToken(refreshToken);
        var session = await db.Sessions
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.RefreshToken == refreshTokenHash, ct);
        if (session is null)
        {
            refreshCookieService.Clear(httpContext.Response);
            return TypedResults.NoContent();
        }

        session.WasRevoked = true;
        await db.SaveChangesAsync(ct);

        refreshCookieService.Clear(httpContext.Response);

        logger.LogInformation("{EmailRef} successfully logged out", UserUtils.DescribeEmailForLogs(session.User.Email));
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.LogoutSucceeded,
            EntityType = "session",
            ActorUserId = session.User.Id,
            ActorEmail = session.User.Email,
            ActorDisplayName = $"{session.User.LastName} {session.User.FirstName}".Trim(),
            Details = "Выход из текущей сессии"
        }, ct);
        return TypedResults.NoContent();
    }
}
