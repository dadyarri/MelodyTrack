using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

[ApiEndpoint(ApiMethod.Delete, "/clients/{id}/portal-links")]
public sealed class RevokeClientPortalLinkEndpoint
{
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> HandleAsync(
        [AsParameters] GetEntityRequest req,
        AppDbContext db,
        IAuditLogService auditLogService,
        ICurrentUserAccessor currentUserAccessor,
        TimeProvider timeProvider,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        var currentUser = await currentUserAccessor.GetAsync(ct)
            ?? throw new InvalidOperationException("The administrator policy succeeded without a current user.");

        var loginLink = await db.ClientPortalLoginLinks
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.User.ClientId == req.Id, ct);

        if (loginLink is null)
        {
            validationErrors.Add(nameof(req.Id), "Кабинет для этого клиента еще не создан.");
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateValidationProblemDetails(
                validationErrors,
                httpContext,
                StatusCodes.Status404NotFound));
        }

        loginLink.RevokedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        loginLink.FailedPinAttempts = 0;
        loginLink.LastFailedPinAttemptAtUtc = null;

        await db.ClientPortalSavedIdentityReferences
            .Where(item => item.LoginLinkId == loginLink.Id)
            .ExecuteDeleteAsync(ct);

        await db.Sessions
            .Where(item => item.User.Id == loginLink.User.Id && !item.WasRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.WasRevoked, true), ct);
        await db.SaveChangesAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.ClientPortalLinkRevoked,
            EntityType = "client_portal_link",
            EntityId = loginLink.Id.ToString(),
            ActorUserId = currentUser.Id,
            ActorEmail = currentUser.Email,
            ActorDisplayName = $"{currentUser.LastName} {currentUser.FirstName}".Trim(),
            Details = AuditDetailsFormatter.DescribeContext("Клиент", $"{loginLink.User.LastName} {loginLink.User.FirstName}".Trim())
        }, ct);

        return TypedResults.NoContent();
    }
}
