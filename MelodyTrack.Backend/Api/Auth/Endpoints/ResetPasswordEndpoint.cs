using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class ResetPasswordEndpoint(AppDbContext db)
    : Ep.Req<ResetPasswordRequest>.Res<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/resetPassword");
        AllowAnonymous();
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(
        ResetPasswordRequest req,
        CancellationToken ct)
    {
        var restoreCode = await db.PasswordRestorationRequests
            .Where(e => !e.WasUsed && e.Token == req.Token)
            .FirstOrDefaultAsync(ct);

        if (restoreCode is null)
        {
            Logger.LogWarning("Password reset attempt with invalid or used token {Token}", req.Token);
            return TypedResults.Forbid();
        }

        var user = await db.Users
            .Where(e => e.Email == restoreCode.Email)
            .FirstOrDefaultAsync(ct);

        if (user is null || user.TotpSecret is not null && req.Otp is null)
        {
            Logger.LogWarning("Password reset attempt for non-existent user or missing 2FA code for user {Email}", restoreCode.Email);
            return TypedResults.Forbid();
        }

        if (user.TotpSecret is not null)
        {
            var secretKey = Base32Encoding.ToBytes(user.TotpSecret);
            var totp = new Totp(secretKey, mode: OtpHashMode.Sha512);
            if (!totp.VerifyTotp(req.Otp, out _, new VerificationWindow(1, 1)))
            {
                Logger.LogWarning("Invalid 2FA code provided during password reset for user {Email}", user.Email);
                return TypedResults.Unauthorized();
            }
        }

        UserUtils.HashPassword(user.Email, req.NewPassword, out var hash);
        user.Password = hash;
        restoreCode.WasUsed = true;
        await db.SaveChangesAsync(ct);

        await db.Sessions.Where(e => e.User == user)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        Logger.LogInformation("Successfully reset password for user {Email} and revoked all sessions", user.Email);
        return TypedResults.NoContent();
    }
}