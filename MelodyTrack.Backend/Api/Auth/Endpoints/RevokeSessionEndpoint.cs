using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class RevokeSessionEndpoint(AppDbContext db, IAuditLogService auditLogService, ICurrentUserAccessor currentUserAccessor)
    : Ep.Req<GetEntityRequest>.Res<Results<NoContent, UnauthorizedHttpResult, NotFound<ProblemDetails>>>
{
    public override void Configure()
    {
        Delete("/auth/sessions/{id}");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, NotFound<ProblemDetails>>> ExecuteAsync(
        GetEntityRequest req,
        CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            Logger.LogWarning("Session revoke attempt without a current user");
            return TypedResults.Unauthorized();
        }

        var revokedCount = await db.Sessions
            .Where(e => e.Id == req.Id && e.User.Id == user.Id && !e.WasRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        if (revokedCount == 0)
        {
            AddError(r => r.Id, "Сессия не найдена");
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status404NotFound));
        }

        Logger.LogInformation("{EmailRef} revoked session {SessionId}", UserUtils.DescribeEmailForLogs(user.Email), req.Id);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "session_revoked",
            EntityType = "session",
            EntityId = req.Id.ToString(),
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            ActorDisplayName = $"{user.LastName} {user.FirstName}".Trim(),
            Details = "Принудительное завершение одной сессии"
        }, ct);
        return TypedResults.NoContent();
    }
}
