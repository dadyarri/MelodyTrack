using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MelodyTrack.Data.Security;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/auth/2fa/setup")]
public sealed class Setup2FaEndpoint
{

    public static async Task<Results<Ok<Setup2FaResponse>, UnauthorizedHttpResult>> HandleAsync(
        Setup2FaRequest req,
        ICurrentUserAccessor currentUserAccessor,
        CredentialHasher credentialHasher,
        ILogger<Setup2FaEndpoint> logger,
        CancellationToken ct
    )
    {
        var user = await currentUserAccessor.GetAsync(ct);

        if (user is null || !credentialHasher.VerifyPassword(user.Password, req.Password))
        {
            logger.LogWarning("2FA setup attempt with invalid current user or password");
            return TypedResults.Unauthorized();
        }

        var (secret, otpUrl) = UserUtils.GenerateTotp(user.Email);

        logger.LogInformation("auth.2fa.setup_started {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
        return TypedResults.Ok(new Setup2FaResponse
        {
            Secret = secret,
            OtpUrl = otpUrl
        });
    }
}
