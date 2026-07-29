using FastEndpoints;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class LogoutEndpoint(
    AppDbContext db,
    IAuditLogService auditLogService,
    ICurrentUserAccessor currentUserAccessor,
    RefreshSessionCookieService refreshCookieService)
    : Ep.NoReq.Res<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/logout");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            Logger.LogWarning("Logout attempt without a current user");
            return TypedResults.Unauthorized();
        }

        var refreshToken = refreshCookieService.ReadRefreshToken(HttpContext.Request);
        if (refreshToken is null)
        {
            refreshCookieService.Clear(HttpContext.Response);
            return TypedResults.Unauthorized();
        }

        if (!refreshCookieService.HasValidCsrfToken(HttpContext.Request, refreshToken))
        {
            Logger.LogWarning("auth.logout.csrf_rejected {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
            return TypedResults.Forbid();
        }

        var refreshTokenHash = UserUtils.HashOpaqueToken(refreshToken);

        var revokedCount = await db.Sessions
            .Where(e => e.RefreshToken == refreshTokenHash && e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        if (revokedCount == 0)
        {
            refreshCookieService.Clear(HttpContext.Response);
            Logger.LogWarning("Logout attempt by {EmailRef} for non-owned or unknown refresh token", UserUtils.DescribeEmailForLogs(user.Email));
            return TypedResults.Unauthorized();
        }

        refreshCookieService.Clear(HttpContext.Response);

        Logger.LogInformation("{EmailRef} successfully logged out", UserUtils.DescribeEmailForLogs(user.Email));
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "logout_succeeded",
            EntityType = "session",
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            ActorDisplayName = $"{user.LastName} {user.FirstName}".Trim(),
            Details = "Выход из текущей сессии"
        }, ct);
        return TypedResults.NoContent();
    }
}
