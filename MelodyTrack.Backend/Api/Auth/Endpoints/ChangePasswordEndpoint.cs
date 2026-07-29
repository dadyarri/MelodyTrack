using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class ChangePasswordEndpoint(
    AppDbContext db,
    IAuditLogService auditLogService,
    ICurrentUserAccessor currentUserAccessor,
    RefreshSessionCookieService refreshCookieService)
    : Ep.Req<ChangePasswordRequest>.Res<Results<NoContent, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/password-change");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult>> ExecuteAsync(ChangePasswordRequest req, CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);

        if (user is null || !UserUtils.IsValidPassword(user.Password, req.CurrentPassword))
        {
            Logger.LogWarning("Password change failed for current user: invalid current password");
            return TypedResults.Unauthorized();
        }

        UserUtils.HashPassword(req.NewPassword, out var hash);
        user.Password = hash;
        await db.SaveChangesAsync(ct);

        await db.Sessions
            .Where(e => e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);
        refreshCookieService.Clear(HttpContext.Response);

        Logger.LogInformation("auth.password_changed {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "password_changed",
            EntityType = "user",
            EntityId = user.Id.ToString(),
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            ActorDisplayName = $"{user.LastName} {user.FirstName}".Trim(),
            Details = "Пароль изменен из профиля"
        }, ct);
        return TypedResults.NoContent();
    }
}
