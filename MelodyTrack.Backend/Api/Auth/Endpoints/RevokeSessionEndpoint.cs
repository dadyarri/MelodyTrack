using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Delete, "/auth/sessions/{id}")]
public sealed class RevokeSessionEndpoint
{
    [Authorize(Policy = AuthorizationPolicies.StaffOrClientPortal)]
    public static async Task<Results<NoContent, UnauthorizedHttpResult, NotFound<ApiProblemDetails>>> HandleAsync(
        [AsParameters] GetEntityRequest req,
        AppDbContext db,
        IAuditLogService auditLogService,
        ICurrentUserAccessor currentUserAccessor,
        RefreshSessionCookieService refreshCookieService,
        ILogger<RevokeSessionEndpoint> logger,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        var user = await currentUserAccessor.GetAsync(ct);
        if (user is null)
        {
            logger.LogWarning("Session revoke attempt without a current user");
            return TypedResults.Unauthorized();
        }

        var revokedCount = await db.Sessions
            .Where(e => e.Id == req.Id && e.User.Id == user.Id && !e.WasRevoked)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        if (revokedCount == 0)
        {
            validationErrors.Add(nameof(req.Id), "Сессия не найдена");
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateValidationProblemDetails(
                validationErrors,
                httpContext,
                StatusCodes.Status404NotFound));
        }

        if (currentUserAccessor.SessionId == req.Id)
        {
            refreshCookieService.Clear(httpContext.Response);
        }

        logger.LogInformation("{EmailRef} revoked session {SessionId}", UserUtils.DescribeEmailForLogs(user.Email), req.Id);
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
