using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Serilog;

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
        Log.Logger.Information("Trying to get information about invite {InviteCode}", req.InviteCode);
        var ulidParsed = Ulid.TryParse(req.InviteCode, out var ulid);

        if (!ulidParsed)
        {
            Log.Logger.Warning("Invite code {InviteCode} could not be parsed", req.InviteCode);
            return TypedResults.Forbid();
        }

        var invite = await db.InviteCodes
            .Where(e => e.Code == ulid && !e.WasUsed && e.ValidUntil >= DateTime.UtcNow)
            .FirstOrDefaultAsync(ct);

        if (invite is null)
        {
            Log.Logger.Warning("Invite code {InviteCode} is invalid", req.InviteCode);
            return TypedResults.Forbid();
        }

        Log.Logger.Information("Invite code {InviteCode} found", req.InviteCode);
        return TypedResults.Ok(new GetInviteCodeInformationResponse
        {
            Email = invite.Email,
        });
    }
}