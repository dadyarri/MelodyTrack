using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MelodyTrack.Data.Security;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/auth/password-change")]
public sealed class ChangePasswordEndpoint
{

    public static async Task<Results<NoContent, UnauthorizedHttpResult>> HandleAsync(
        ChangePasswordRequest req,
        AppDbContext db,
        IAuditLogService auditLogService,
        ICurrentUserAccessor currentUserAccessor,
        RefreshSessionCookieService refreshCookieService,
        CredentialHasher credentialHasher,
        ILogger<ChangePasswordEndpoint> logger,
        HttpContext httpContext,
        CancellationToken ct
    )
    {
        var user = await currentUserAccessor.GetAsync(ct);

        if (user is null || !credentialHasher.VerifyPassword(user.Password, req.CurrentPassword))
        {
            logger.LogWarning("Password change failed for current user: invalid current password");
            return TypedResults.Unauthorized();
        }

        user.Password = credentialHasher.HashPassword(req.NewPassword);
        await db.SaveChangesAsync(ct);

        await db.Sessions
            .Where(e => e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);
        refreshCookieService.Clear(httpContext.Response);

        logger.LogInformation("auth.password_changed {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.PasswordChanged,
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
