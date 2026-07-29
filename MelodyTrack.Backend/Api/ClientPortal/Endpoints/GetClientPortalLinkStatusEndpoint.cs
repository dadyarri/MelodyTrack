using FastEndpoints;
using MelodyTrack.Backend.Api.ClientPortal.Requests;
using MelodyTrack.Backend.Api.ClientPortal.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ClientPortal.Endpoints;

public class GetClientPortalLinkStatusEndpoint(AppDbContext db)
    : Ep.Req<GetClientPortalLinkStatusRequest>.Res<Results<Ok<GetClientPortalLinkStatusResponse>, ApiProblemDetails>>
{
    public override void Configure()
    {
        Get("/client-portal/auth/link");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting(ApiRateLimitPolicies.PortalLinkStatus));
        Description(builder => builder.Produces<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, ApiMediaTypes.ProblemJson));
    }

    public override async Task<Results<Ok<GetClientPortalLinkStatusResponse>, ApiProblemDetails>> ExecuteAsync(GetClientPortalLinkStatusRequest req, CancellationToken ct)
    {
        var tokenHash = UserUtils.HashOpaqueToken(req.Token);
        var link = await db.ClientPortalLoginLinks
            .AsNoTracking()
            .Include(item => item.User)
                .ThenInclude(item => item.Role)
            .FirstOrDefaultAsync(item => item.TokenHash == tokenHash && item.RevokedAtUtc == null, ct);

        if (link is null || !link.User.Role.RoleName.IsClient() || link.User.ClientId is null)
        {
            AddError(item => item.Token, "Ссылка входа недействительна. Попросите администратора проверить ссылку.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status403Forbidden);
        }

        return TypedResults.Ok(new GetClientPortalLinkStatusResponse
        {
            FirstName = link.User.FirstName,
            HasPin = !string.IsNullOrWhiteSpace(link.PinHash)
        });
    }
}
