using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class LogoutEndpoint(AppDbContext db, IAuditLogService auditLogService) : Ep.Req<LogoutRequest>.Res<Results<NoContent, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/logout");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult>> ExecuteAsync(LogoutRequest req,
        CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (email is null)
        {
            Logger.LogWarning("Logout attempt without valid email claim in token");
            return TypedResults.Unauthorized();
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null)
        {
            Logger.LogWarning("Logout attempt for non-existent user with email {Email}", email.Value);
            return TypedResults.Unauthorized();
        }

        var revokedCount = await db.Sessions
            .Where(e => e.RefreshToken == req.RefreshToken && e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        if (revokedCount == 0)
        {
            Logger.LogWarning("Logout attempt by {Email} for non-owned or unknown refresh token", email.Value);
            return TypedResults.Unauthorized();
        }

        Logger.LogInformation("User {Email} successfully logged out", email.Value);
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
