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

public class AuthenticateClientPortalLinkEndpoint(
    AppDbContext db,
    IAuditLogService auditLogService,
    ClientPortalSessionService sessionService,
    TimeProvider timeProvider)
    : Ep.Req<AuthenticateClientPortalLinkRequest>.Res<Results<Ok<ClientPortalAuthenticationResponse>, ApiProblemDetails>>
{
    public override void Configure()
    {
        Post("/client-portal/auth/link");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting(ApiRateLimitPolicies.PortalAuthentication));
        Description(builder => builder.Produces<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, ApiMediaTypes.ProblemJson));
    }

    public override async Task<Results<Ok<ClientPortalAuthenticationResponse>, ApiProblemDetails>> ExecuteAsync(AuthenticateClientPortalLinkRequest req, CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var link = await LoadActiveLinkAsync(req.Token, ct);
        if (link is null)
        {
            AddError(item => item.Token, "Ссылка входа недействительна. Попросите администратора проверить ссылку.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status403Forbidden);
        }

        if (!link.User.Role.RoleName.IsClient() || link.User.ClientId is null)
        {
            AddError(item => item.Token, "Для этой ссылки не найден клиентский аккаунт.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(link.PinHash))
        {
            if (string.IsNullOrWhiteSpace(req.PinConfirmation))
            {
                AddError(item => item.PinConfirmation, "Подтвердите PIN-код.");
                return ApiErrorResponseFactory.CreateValidationProblemDetails(
                    ValidationFailures,
                    HttpContext,
                    StatusCodes.Status400BadRequest);
            }

            if (!string.Equals(req.Pin, req.PinConfirmation, StringComparison.Ordinal))
            {
                AddError(item => item.PinConfirmation, "PIN-коды не совпадают.");
                return ApiErrorResponseFactory.CreateValidationProblemDetails(
                    ValidationFailures,
                    HttpContext,
                    StatusCodes.Status400BadRequest);
            }

            UserUtils.HashPassword(req.Pin, out var pinHash);
            link.PinHash = pinHash;
            link.PinSetAtUtc = nowUtc;
        }
        else if (!UserUtils.IsValidPassword(link.PinHash, req.Pin))
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
                Details = $"Неудачных попыток с момента последнего успешного входа: {link.FailedPinAttempts}"
            }, ct);
            AddError(item => item.Pin, "PIN-код неверный.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status401Unauthorized);
        }

        link.FailedPinAttempts = 0;
        link.LastFailedPinAttemptAtUtc = null;

        var savedReference = UserUtils.GenerateRandomString(32);
        var savedIdentity = new Data.Models.ClientPortalSavedIdentityReference
        {
            Id = Ulid.NewUlid(),
            LoginLink = link,
            LoginLinkId = link.Id,
            ReferenceHash = UserUtils.HashOpaqueToken(savedReference),
            CreatedAtUtc = nowUtc,
            LastUsedAtUtc = nowUtc
        };
        await db.ClientPortalSavedIdentityReferences.AddAsync(savedIdentity, ct);
        var accessToken = await sessionService.IssueAsync(link.User, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new ClientPortalAuthenticationResponse
        {
            AccessToken = accessToken,
            FirstName = link.User.FirstName,
            LastName = link.User.LastName,
            SavedIdentity = SavedClientPortalIdentityMapper.ToResponse(link.User, savedReference, nowUtc)
        });
    }

    private async Task<Data.Models.ClientPortalLoginLink?> LoadActiveLinkAsync(string token, CancellationToken ct)
    {
        var tokenHash = UserUtils.HashOpaqueToken(token);
        return await db.ClientPortalLoginLinks
            .Include(item => item.User)
                .ThenInclude(item => item.Role)
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash && item.RevokedAtUtc == null, ct);
    }
}
