using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.ErrorHandling;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/auth/invites")]
public sealed class GetInviteCodeInformationEndpoint
{

        [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.InviteInformation)]
    public static async Task<Results<Ok<GetInviteCodeInformationResponse>, ApiProblemDetails>> HandleAsync(
        [AsParameters] GetInviteCodeInformationRequest req,
        AppDbContext db,
        TimeProvider timeProvider,
        ILogger<GetInviteCodeInformationEndpoint> logger,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        logger.LogInformation("Trying to get information about invite {InviteCode}", req.InviteCode);
        var ulidParsed = Ulid.TryParse(req.InviteCode, out var ulid);

        if (!ulidParsed)
        {
            logger.LogWarning("Invite code {InviteCode} could not be parsed", req.InviteCode);
            validationErrors.Add(nameof(req.InviteCode), "Ссылка приглашения недействительна. Попросите администратора создать новую.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                validationErrors,
                httpContext,
                StatusCodes.Status403Forbidden);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var invite = await db.InviteCodes
            .Where(e => e.Code == ulid && !e.WasUsed && e.ValidUntil >= nowUtc)
            .FirstOrDefaultAsync(ct);

        if (invite is null)
        {
            logger.LogWarning("Invite code {InviteCode} is invalid", req.InviteCode);
            validationErrors.Add(nameof(req.InviteCode), "Ссылка приглашения недействительна или уже просрочена. Попросите администратора создать новую.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                validationErrors,
                httpContext,
                StatusCodes.Status403Forbidden);
        }

        logger.LogInformation("Invite code {InviteCode} found", req.InviteCode);
        return TypedResults.Ok(new GetInviteCodeInformationResponse
        {
            Email = invite.Email
        });
    }
}
