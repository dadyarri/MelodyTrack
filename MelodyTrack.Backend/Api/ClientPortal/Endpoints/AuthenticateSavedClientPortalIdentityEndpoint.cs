using FastEndpoints;
using MelodyTrack.Backend.Api.ClientPortal.Requests;
using MelodyTrack.Backend.Api.ClientPortal.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ClientPortal.Endpoints;

public class AuthenticateSavedClientPortalIdentityEndpoint(
    AppDbContext db,
    IAuditLogService auditLogService,
    ClientPortalSessionService sessionService,
    TimeProvider timeProvider)
    : Ep.Req<AuthenticateSavedClientPortalIdentityRequest>.Res<Results<Ok<ClientPortalAuthenticationResponse>, ApiProblemDetails>>
{
    public override void Configure()
    {
        Post("/client-portal/auth/saved");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting(ApiRateLimitPolicies.PortalAuthentication));
        Description(builder => builder.Produces<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, ApiMediaTypes.ProblemJson));
    }

    public override async Task<Results<Ok<ClientPortalAuthenticationResponse>, ApiProblemDetails>> ExecuteAsync(
        AuthenticateSavedClientPortalIdentityRequest req,
        CancellationToken ct)
    {
        var referenceHash = UserUtils.HashOpaqueToken(req.Reference);
        var savedIdentity = await db.ClientPortalSavedIdentityReferences
            .Include(item => item.LoginLink)
                .ThenInclude(item => item.User)
                    .ThenInclude(item => item.Role)
            .FirstOrDefaultAsync(item => item.ReferenceHash == referenceHash, ct);

        if (savedIdentity is null ||
            savedIdentity.LoginLink.RevokedAtUtc is not null ||
            !savedIdentity.LoginLink.User.Role.RoleName.IsClient() ||
            savedIdentity.LoginLink.User.ClientId is null ||
            string.IsNullOrWhiteSpace(savedIdentity.LoginLink.PinHash))
        {
            AddError(item => item.Reference, "Сохраненный профиль больше недоступен. Удалите его или откройте новую ссылку от преподавателя.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status403Forbidden);
        }

        var link = savedIdentity.LoginLink;
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        if (!UserUtils.IsValidPassword(link.PinHash, req.Pin))
        {
            link.FailedPinAttempts++;
            link.LastFailedPinAttemptAtUtc = nowUtc;
            await db.SaveChangesAsync(ct);
            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Category = "security",
                Action = link.FailedPinAttempts >= 3 ? "portal_pin_repeated_failures" : "portal_pin_failed",
                EntityType = "client_portal_link",
                EntityId = link.Id.ToString(),
                ActorUserId = link.User.Id,
                ActorEmail = link.User.Email,
                ActorDisplayName = $"{link.User.LastName} {link.User.FirstName}".Trim(),
                Details = $"Неудачных попыток с момента последнего успешного входа: {link.FailedPinAttempts}; источник: сохраненный профиль"
            }, ct);
            AddError(item => item.Pin, "PIN-код неверный.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status401Unauthorized);
        }

        link.FailedPinAttempts = 0;
        link.LastFailedPinAttemptAtUtc = null;
        savedIdentity.LastUsedAtUtc = nowUtc;
        var accessToken = await sessionService.IssueAsync(link.User, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new ClientPortalAuthenticationResponse
        {
            AccessToken = accessToken,
            FirstName = link.User.FirstName,
            LastName = link.User.LastName,
            SavedIdentity = SavedClientPortalIdentityMapper.ToResponse(link.User, req.Reference, nowUtc)
        });
    }
}
