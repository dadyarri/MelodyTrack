using FastEndpoints;
using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Api.ClientPortal.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UaDetector;

namespace MelodyTrack.Backend.Api.ClientPortal.Endpoints;

public class AuthenticateClientPortalLinkEndpoint(
    AppDbContext db,
    IUaDetector uaDetector,
    IAuditLogService auditLogService,
    RefreshSessionCookieService refreshCookieService,
    TimeProvider timeProvider)
    : Ep.Req<AuthenticateClientPortalLinkRequest>.Res<Results<Ok<LoginResponse>, ApiProblemDetails>>
{
    public override void Configure()
    {
        Post("/client-portal/auth/link");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting(ApiRateLimitPolicies.PortalAuthentication));
        Description(builder => builder.Produces<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, ApiMediaTypes.ProblemJson));
    }

    public override async Task<Results<Ok<LoginResponse>, ApiProblemDetails>> ExecuteAsync(AuthenticateClientPortalLinkRequest req, CancellationToken ct)
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

        await db.Sessions
            .Where(item => item.User.Id == link.User.Id && !item.WasRevoked)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.WasRevoked, true), ct);

        var refreshToken = UserUtils.GenerateRandomString(32);
        var session = new Data.Models.Session
        {
            Id = Ulid.NewUlid(),
            User = link.User,
            RefreshToken = UserUtils.HashOpaqueToken(refreshToken),
            DeviceInfo = BrowserUtils.GetDeviceInfo(HttpContext.Request.Headers, uaDetector),
            ValidUntil = nowUtc.AddDays(30)
        };

        await db.Sessions.AddAsync(session, ct);
        await db.SaveChangesAsync(ct);
        refreshCookieService.Issue(HttpContext.Response, refreshToken, session.ValidUntil);

        return TypedResults.Ok(new LoginResponse
        {
            AccessToken = UserUtils.CreateAccessToken(link.User, session.Id, timeProvider),
            FirstName = link.User.FirstName,
            LastName = link.User.LastName
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
