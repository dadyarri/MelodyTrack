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

[ApiEndpoint(ApiMethod.Get, "/client-portal/auth/saved")]
public sealed class GetSavedClientPortalIdentityStatusEndpoint
{

        [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.PortalLinkStatus)]
    public static async Task<Results<Ok<GetSavedClientPortalIdentityStatusResponse>, ApiProblemDetails>> HandleAsync(
        [AsParameters] GetSavedClientPortalIdentityStatusRequest req,
        AppDbContext db,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        var referenceHash = UserUtils.HashOpaqueToken(req.Reference);
        var savedIdentity = await db.ClientPortalSavedIdentityReferences
            .AsNoTracking()
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
            validationErrors.Add(nameof(req.Reference), "Сохраненный профиль больше недоступен. Удалите его или откройте новую ссылку от преподавателя.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                validationErrors,
                httpContext,
                StatusCodes.Status403Forbidden);
        }

        return TypedResults.Ok(new GetSavedClientPortalIdentityStatusResponse
        {
            DisplayLabel = SavedClientPortalIdentityMapper.BuildDisplayLabel(
                savedIdentity.LoginLink.User.FirstName,
                savedIdentity.LoginLink.User.LastName)
        });
    }
}
