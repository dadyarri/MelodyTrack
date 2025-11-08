using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class CheckIf2FaEnabledEndpoint(AppDbContext db)
    : Ep.Req<CheckIf2FaEnabledRequest>.Res<Ok<CheckIf2FaEnabledResponse>>
{
    public override void Configure()
    {
        Get("/auth/2fa/enabled");
        AllowAnonymous();
    }

    public override async Task<Ok<CheckIf2FaEnabledResponse>> ExecuteAsync(CheckIf2FaEnabledRequest req,
        CancellationToken ct)
    {
        Logger.LogDebug("Checking 2FA status for user {Email}", req.Email);
        var user = await db.Users.FirstOrDefaultAsync(e => e.Email == req.Email, ct);

        var is2FaEnabled = user?.TotpSecret is not null;
        Logger.LogInformation("2FA status for user {Email}: {Status}", req.Email, is2FaEnabled ? "enabled" : "disabled");

        return TypedResults.Ok(new CheckIf2FaEnabledResponse
        {
            Enabled = is2FaEnabled
        });
    }
}