using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class Setup2FaEndpoint(ICurrentUserAccessor currentUserAccessor)
    : Ep.Req<Setup2FaRequest>.Res<Results<Ok<Setup2FaResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/2fa/setup");
    }

    public override async Task<Results<Ok<Setup2FaResponse>, UnauthorizedHttpResult>> ExecuteAsync(Setup2FaRequest req,
        CancellationToken ct)
    {
        var user = await currentUserAccessor.GetAsync(ct);

        if (user is null || !UserUtils.IsValidPassword(user.Password, req.Password))
        {
            Logger.LogWarning("2FA setup attempt with invalid current user or password");
            return TypedResults.Unauthorized();
        }

        var (secret, otpUrl) = UserUtils.GenerateTotp(user.Email);

        Logger.LogInformation("auth.2fa.setup_started {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
        return TypedResults.Ok(new Setup2FaResponse
        {
            Secret = secret,
            OtpUrl = otpUrl
        });
    }
}
