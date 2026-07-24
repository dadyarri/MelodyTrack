using FastEndpoints;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class Remove2FaEndpoint(AppDbContext db, IAuditLogService auditLogService, ICurrentUserAccessor currentUserAccessor)
    : Ep.NoReq.Res<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Delete("/auth/2fa/delete");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(
        CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            Logger.LogWarning("2FA removal attempt without a current user");
            return TypedResults.Unauthorized();
        }

        if (user.Role.RoleName.IsAnyAdmin())
        {
            Logger.LogWarning("Attempt to remove 2FA for admin {EmailRef} - operation not allowed", UserUtils.DescribeEmailForLogs(user.Email));
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

        Logger.LogInformation("auth.2fa.removed {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "two_factor_removed",
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
