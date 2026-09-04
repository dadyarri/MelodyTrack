using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Delete, "/auth/2fa")]
public sealed class Remove2FaEndpoint
{

    public static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        AppDbContext db,
        IAuditLogService auditLogService,
        ICurrentUserAccessor currentUserAccessor,
        RefreshSessionCookieService refreshCookieService,
        ILogger<Remove2FaEndpoint> logger,
        HttpContext httpContext,
        CancellationToken ct
    )
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            logger.LogWarning("2FA removal attempt without a current user");
            return TypedResults.Unauthorized();
        }

        if (user.Role.RoleName.IsAnyAdmin())
        {
            logger.LogWarning("Attempt to remove 2FA for admin {EmailRef} - operation not allowed", UserUtils.DescribeEmailForLogs(user.Email));
            return TypedResults.Forbid();
        }

        await db.RecoveryCodes
            .Where(e => e.User.Id == user.Id && !e.WasUsed)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasUsed, true), ct);

        await db.Sessions
            .Where(e => e.User.Id == user.Id && !e.WasRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        user.TotpSecret = null;
        await db.SaveChangesAsync(ct);
        refreshCookieService.Clear(httpContext.Response);

        logger.LogInformation("auth.2fa.removed {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.TwoFactorRemoved,
            EntityType = "user",
            EntityId = user.Id.ToString(),
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            ActorDisplayName = $"{user.LastName} {user.FirstName}".Trim(),
            Details = "2FA отключена, активные сессии завершены, коды восстановления отозваны"
        }, ct);
        return TypedResults.NoContent();
    }
}
