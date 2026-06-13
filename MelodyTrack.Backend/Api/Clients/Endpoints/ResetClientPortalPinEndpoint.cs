using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class ResetClientPortalPinEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<GetEntityRequest>.Res<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>>>
{
    public override void Configure()
    {
        Post("/clients/{id}/portal-pin/reset");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>>> ExecuteAsync(
        GetEntityRequest req,
        CancellationToken ct)
    {
        var currentUser = await EndpointAuthUtils.GetCurrentUserContextAsync(User, db, ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUser.Role.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var loginLink = await db.ClientPortalLoginLinks
            .Include(item => item.User)
            .FirstOrDefaultAsync(item => item.User.ClientId == req.Id, ct);

        if (loginLink is null)
        {
            AddError(item => item.Id, "Клиентский кабинет для этого клиента еще не создан.");
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status404NotFound));
        }

        loginLink.PinCode = null;
        loginLink.PinSetAtUtc = null;

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
            Details = $"Сброшен PIN клиентского кабинета для клиента {loginLink.User.LastName} {loginLink.User.FirstName}".Trim()
        }, ct);

        return TypedResults.NoContent();
    }
}
