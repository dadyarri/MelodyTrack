using FastEndpoints;
using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Api.Auth.Responses;
using MelodyTrack.Common.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class GetInviteCodeInformationEndpoint(AppDbContext db)
    : Ep.Req<GetInviteCodeInformationRequest>.Res<Results<Ok<GetInviteCodeInformationResponse>, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/auth/invite");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<GetInviteCodeInformationResponse>, ForbidHttpResult>> ExecuteAsync(
        GetInviteCodeInformationRequest req,
        CancellationToken ct)
    {
        Logger.LogInformation("Trying to get information about invite {InviteCode}", req.InviteCode);
        var ulidParsed = Ulid.TryParse(req.InviteCode, out var ulid);

        if (!ulidParsed)
        {
            Logger.LogWarning("Invite code {InviteCode} could not be parsed", req.InviteCode);
            return TypedResults.Forbid();
        }

        var invite = await db.InviteCodes
            .Where(e => e.Code == ulid && !e.WasUsed && e.ValidUntil >= DateTime.UtcNow)
            .FirstOrDefaultAsync(ct);

        if (invite is null)
        {
            Logger.LogWarning("Invite code {InviteCode} is invalid", req.InviteCode);
            return TypedResults.Forbid();
        }

        Logger.LogInformation("Invite code {InviteCode} found", req.InviteCode);
        return TypedResults.Ok(new GetInviteCodeInformationResponse
        {
            Email = invite.Email
        });
    }
}