using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.ErrorHandling;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class GetInviteCodeInformationEndpoint(AppDbContext db, TimeProvider timeProvider)
    : Ep.Req<GetInviteCodeInformationRequest>.Res<Results<Ok<GetInviteCodeInformationResponse>, ApiProblemDetails>>
{
    public override void Configure()
    {
        Get("/auth/invites");
        AllowAnonymous();
        Options(builder => builder.RequireRateLimiting(ApiRateLimitPolicies.InviteInformation));
        Description(builder => builder.Produces<ApiProblemDetails>(StatusCodes.Status429TooManyRequests, ApiMediaTypes.ProblemJson));
    }

    public override async Task<Results<Ok<GetInviteCodeInformationResponse>, ApiProblemDetails>> ExecuteAsync(
        GetInviteCodeInformationRequest req,
        CancellationToken ct)
    {
        Logger.LogInformation("Trying to get information about invite {InviteCode}", req.InviteCode);
        var ulidParsed = Ulid.TryParse(req.InviteCode, out var ulid);

        if (!ulidParsed)
        {
            Logger.LogWarning("Invite code {InviteCode} could not be parsed", req.InviteCode);
            AddError(r => r.InviteCode, "Ссылка приглашения недействительна. Попросите администратора создать новую.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status403Forbidden);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var invite = await db.InviteCodes
            .Where(e => e.Code == ulid && !e.WasUsed && e.ValidUntil >= nowUtc)
            .FirstOrDefaultAsync(ct);

        if (invite is null)
        {
            Logger.LogWarning("Invite code {InviteCode} is invalid", req.InviteCode);
            AddError(r => r.InviteCode, "Ссылка приглашения недействительна или уже просрочена. Попросите администратора создать новую.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status403Forbidden);
        }

        Logger.LogInformation("Invite code {InviteCode} found", req.InviteCode);
        return TypedResults.Ok(new GetInviteCodeInformationResponse
        {
            Email = invite.Email
        });
    }
}
