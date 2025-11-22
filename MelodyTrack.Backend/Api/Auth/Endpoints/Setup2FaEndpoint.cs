using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Api.Auth.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class Setup2FaEndpoint(AppDbContext db)
    : Ep.Req<Setup2FaRequest>.Res<IResult>
{
    public override void Configure()
    {
        Post("/auth/2fa/setup");
    }

    public override async Task<IResult> ExecuteAsync(Setup2FaRequest req,
        CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (email is null)
        {
            Logger.LogWarning("2FA setup attempt without valid email claim in token");
            return ApiResults.Unauthorized();
        }

        var user = await db.Users.FirstOrDefaultAsync(e => e.Email == email.Value, ct);

        if (user is null || !UserUtils.IsValidPassword(user.Password, req.Password))
        {
            Logger.LogWarning("2FA setup attempt with invalid user or password for email {Email}", email.Value);
            return ApiResults.Unauthorized();
        }

        var (secret, otpUrl) = UserUtils.GenerateTotp(user.Email);

        Logger.LogInformation("Successfully generated 2FA setup information for user {Email}", user.Email);

        return ApiResults.Ok(new Setup2FaResponse
        {
            Secret = secret,
            OtpUrl = otpUrl
        });
    }
}