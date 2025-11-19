using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class Verify2FaEndpoint(AppDbContext db)
    : Ep.Req<Verify2FaRequest>.Res<Results<NoContent, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/2fa/verify");
        AllowAnonymous();
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult>> ExecuteAsync(
        Verify2FaRequest req, CancellationToken ct)
    {
        var email = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value ?? req.Email;

        if (email is null)
        {
            Logger.LogWarning("2FA verification attempt without email");
            return TypedResults.Unauthorized();
        }

        var user = await db.Users.FirstOrDefaultAsync(e => e.Email == email, ct);

        if (user is null)
        {
            Logger.LogWarning("2FA verification attempt for non-existent user with email {Email}", email);
            return TypedResults.Unauthorized();
        }

        var secretKey = Base32Encoding.ToBytes(req.OtpSecret);
        var totp = new Totp(secretKey, mode: OtpHashMode.Sha512);
        if (!totp.VerifyTotp(req.Otp, out _, new VerificationWindow(1, 1)))
        {
            Logger.LogWarning("Invalid 2FA code provided for user {Email}", email);
            return TypedResults.Unauthorized();
        }

        user.TotpSecret = req.OtpSecret;
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("Successfully verified and set up 2FA for user {Email}", email);
        return TypedResults.NoContent();
    }
}