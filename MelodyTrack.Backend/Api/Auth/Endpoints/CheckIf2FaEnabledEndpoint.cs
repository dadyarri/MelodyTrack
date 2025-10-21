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
        var user = await db.Users.FirstOrDefaultAsync(e => e.Email == req.Email, ct);

        return TypedResults.Ok(new CheckIf2FaEnabledResponse
        {
            Enabled = user?.TotpSecret is not null
        });
    }
}