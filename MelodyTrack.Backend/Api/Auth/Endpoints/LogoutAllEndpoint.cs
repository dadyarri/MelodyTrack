using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Authorization;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/auth/logout-all")]
public sealed class LogoutAllEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<UnauthorizedHttpResult, NoContent>> HandleAsync(
        AppDbContext db,
        IAuditLogService auditLogService,
        ICurrentUserAccessor currentUserAccessor,
        RefreshSessionCookieService refreshCookieService,
        ILogger<LogoutAllEndpoint> logger,
        HttpContext httpContext,
        CancellationToken ct
    )
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            logger.LogWarning("Logout all attempt without a current user");
            return TypedResults.Unauthorized();
        }

        await db.Sessions
            .Where(e => e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);
        refreshCookieService.Clear(httpContext.Response);

        logger.LogInformation("auth.logout_all.succeeded {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.LogoutAllSucceeded,
            EntityType = "session",
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            ActorDisplayName = $"{user.LastName} {user.FirstName}".Trim(),
            Details = "Выход изо всех сессий"
        }, ct);
        return TypedResults.NoContent();
    }
}
