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
            return TypedResults.Forbid();
        }

        var user = await db.Users
            .Where(e => e.Email == restoreCode.Email)
            .FirstOrDefaultAsync(ct);

        if (user is null || (user.TotpSecret is not null && req.Otp is null))
        {
            return TypedResults.Forbid();
        }

        if (user.TotpSecret is not null)
        {
            var secretKey = Base32Encoding.ToBytes(user.TotpSecret);
            var totp = new Totp(secretKey, mode: OtpHashMode.Sha512);
            if (!totp.VerifyTotp(req.Otp, out _, new VerificationWindow(1, 1)))
            {
                return TypedResults.Unauthorized();
            }
        }

        UserUtils.HashPassword(user.Email, req.NewPassword, out var hash);
        user.Password = hash;
        restoreCode.WasUsed = true;
        await db.SaveChangesAsync(ct);

        await db.Sessions.Where(e => e.User == user)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);


        return TypedResults.NoContent();
    }
}