using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.ClientPortal.Requests;
using MelodyTrack.Backend.Api.ClientPortal.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ClientPortal.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/client-portal/auth/link")]
public sealed class GetClientPortalLinkStatusEndpoint
{

        [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.PortalLinkStatus)]
    public static async Task<Results<Ok<GetClientPortalLinkStatusResponse>, ApiProblemDetails>> HandleAsync(
        [AsParameters] GetClientPortalLinkStatusRequest req,
        AppDbContext db,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        var tokenHash = UserUtils.HashOpaqueToken(req.Token);
        var link = await db.ClientPortalLoginLinks
            .AsNoTracking()
            .Include(item => item.User)
                .ThenInclude(item => item.Role)
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash && item.RevokedAtUtc == null, ct);

        if (link is null || !link.User.Role.RoleName.IsClient() || link.User.ClientId is null)
        {
            validationErrors.Add(nameof(req.Token), "Ссылка входа недействительна. Попросите администратора проверить ссылку.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                validationErrors,
                httpContext,
                StatusCodes.Status403Forbidden);
        }

        return TypedResults.Ok(new GetClientPortalLinkStatusResponse
        {
            FirstName = link.User.FirstName,
            HasPin = !string.IsNullOrWhiteSpace(link.PinHash)
        });
    }
}
