using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Api.ClientPortal.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UaDetector;

namespace MelodyTrack.Backend.Api.ClientPortal.Endpoints;

public class AuthenticateClientPortalLinkEndpoint(AppDbContext db, IUaDetector uaDetector)
    : Ep.Req<AuthenticateClientPortalLinkRequest>.Res<Results<Ok<LoginResponse>, ProblemDetails>>
{
    public override void Configure()
    {
        Post("/client-portal/auth/link");
        AllowAnonymous();
        Throttle(20, 60);
    }

    public override async Task<Results<Ok<LoginResponse>, ProblemDetails>> ExecuteAsync(AuthenticateClientPortalLinkRequest req, CancellationToken ct)
    {
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

        if (string.IsNullOrWhiteSpace(link.PinCode))
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

            link.PinCode = req.Pin;
            link.PinSetAtUtc = DateTime.UtcNow;
        }
        else if (!string.Equals(link.PinCode, req.Pin, StringComparison.Ordinal))
        {
            AddError(item => item.Pin, "PIN-код неверный.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status401Unauthorized);
        }

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
            ValidUntil = DateTime.UtcNow.AddDays(30)
        };

        await db.Sessions.AddAsync(session, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new LoginResponse
        {
            AccessToken = UserUtils.CreateAccessToken(link.User, session.Id),
            RefreshToken = refreshToken,
            FirstName = link.User.FirstName,
            LastName = link.User.LastName
        });
    }

    private async Task<Data.Models.ClientPortalLoginLink?> LoadActiveLinkAsync(string token, CancellationToken ct)
    {
        if (!UserUtils.TryReadClientPortalToken(token, out var clientId))
        {
            return null;
        }

        return await db.ClientPortalLoginLinks
            .Include(item => item.User)
                .ThenInclude(item => item.Role)
            .FirstOrDefaultAsync(item => item.User.ClientId == clientId, ct);
    }
}
