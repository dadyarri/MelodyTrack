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

public class GetSavedClientPortalIdentityStatusEndpoint(AppDbContext db)
    : Ep.Req<GetSavedClientPortalIdentityStatusRequest>.Res<Results<Ok<GetSavedClientPortalIdentityStatusResponse>, ApiProblemDetails>>
{
    public override void Configure()
    {
        Get("/client-portal/auth/saved");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting(ApiRateLimitPolicies.PortalLinkStatus));
        Description(builder => builder.Produces<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, ApiMediaTypes.ProblemJson));
    }

    public override async Task<Results<Ok<GetSavedClientPortalIdentityStatusResponse>, ApiProblemDetails>> ExecuteAsync(
        GetSavedClientPortalIdentityStatusRequest req,
        CancellationToken ct)
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
            AddError(item => item.Reference, "Сохраненный профиль больше недоступен. Удалите его или откройте новую ссылку от преподавателя.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
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
