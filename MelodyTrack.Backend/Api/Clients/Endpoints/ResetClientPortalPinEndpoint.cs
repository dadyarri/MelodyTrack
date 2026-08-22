using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/clients/{id}/portal-pin-resets")]
public sealed class ResetClientPortalPinEndpoint
{

    public static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> HandleAsync(
        [AsParameters] GetEntityRequest req,
        AppDbContext db,
        IAuditLogService auditLogService,
        ICurrentUserAccessor currentUserAccessor,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUser.Role.RoleName.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

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

        loginLink.PinHash = null;
        loginLink.PinSetAtUtc = null;
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
            Category = "clients",
            Action = "client_portal_pin_reset",
            EntityType = "client_portal_link",
            EntityId = loginLink.Id.ToString(),
            ActorUserId = currentUser.Id,
            ActorEmail = currentUser.Email,
            Details = AuditDetailsFormatter.DescribeContext("Клиент", $"{loginLink.User.LastName} {loginLink.User.FirstName}".Trim())
        }, ct);

        return TypedResults.NoContent();
    }
}
