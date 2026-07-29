using FastEndpoints;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class LogoutAllEndpoint(
    AppDbContext db,
    IAuditLogService auditLogService,
    ICurrentUserAccessor currentUserAccessor,
    RefreshSessionCookieService refreshCookieService)
    : Ep.NoReq.Res<Results<UnauthorizedHttpResult, NoContent>>
{
    public override void Configure()
    {
        Post("/auth/logout-all");
    }

    public override async Task<Results<UnauthorizedHttpResult, NoContent>> ExecuteAsync(CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            Logger.LogWarning("Logout all attempt without a current user");
            return TypedResults.Unauthorized();
        }

        await db.Sessions
            .Where(e => e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);
        refreshCookieService.Clear(HttpContext.Response);

        Logger.LogInformation("auth.logout_all.succeeded {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "logout_all_succeeded",
            EntityType = "session",
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            ActorDisplayName = $"{user.LastName} {user.FirstName}".Trim(),
            Details = "Выход изо всех сессий"
        }, ct);
        return TypedResults.NoContent();
    }
}
