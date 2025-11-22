using FastEndpoints;
using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Api.Auth.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class GetInviteCodeInformationEndpoint(AppDbContext db)
    : Ep.Req<GetInviteCodeInformationRequest>.Res<IResult>
{
    public override void Configure()
    {
        Get("/auth/invite");
        AllowAnonymous();
    }

    public override async Task<IResult> ExecuteAsync(
        GetInviteCodeInformationRequest req,
        CancellationToken ct)
    {
        Logger.LogInformation("Trying to get information about invite {InviteCode}", req.InviteCode);
        var ulidParsed = Ulid.TryParse(req.InviteCode, out var ulid);

        if (!ulidParsed)
        {
            Logger.LogWarning("Invite code {InviteCode} could not be parsed", req.InviteCode);
            return ApiResults.Forbid("Невалидный код приглашения", "INVALID_INVITE_CODE");
        }

        var invite = await db.InviteCodes
            .Where(e => e.Code == ulid && !e.WasUsed && e.ValidUntil >= DateTime.UtcNow)
            .FirstOrDefaultAsync(ct);

        if (invite is null)
        {
            Logger.LogWarning("Invite code {InviteCode} is invalid", req.InviteCode);
            return ApiResults.Forbid("Невалидный код приглашения", "INVALID_INVITE_CODE");
        }

        Logger.LogInformation("Invite code {InviteCode} found", req.InviteCode);
        return ApiResults.Ok(new GetInviteCodeInformationResponse
        {
            Email = invite.Email
        });
    }
}