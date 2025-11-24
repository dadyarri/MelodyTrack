using FastEndpoints;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Api.Auth.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class Recover2FaEndpoint(AppDbContext db)
    : Ep.Req<Recover2FaRequest>.Res<IResult>
{
    public override void Configure()
    {
        Post("/auth/2fa/recover");
        AllowAnonymous();
    }

    public override async Task<IResult> ExecuteAsync(
        Recover2FaRequest req,
        CancellationToken ct)
    {
        var user = await db.Users
            .Where(e => e.Email == req.Email)
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            Logger.LogWarning("2FA recovery attempt for non-existent user with email {Email}", req.Email);
            return ApiResults.Unauthorized();
        }

        var recoveryCode = await db.RecoveryCodes
            .Where(e => e.Code == req.RecoveryCode && !e.WasUsed)
            .FirstOrDefaultAsync(ct);

        if (recoveryCode is null)
        {
            Logger.LogWarning("2FA recovery attempt with invalid or used recovery code for user {Email}", req.Email);
            return ApiResults.Forbid();
        }

        await db.Sessions
            .Where(e => e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        var refreshToken = UserUtils.GenerateRandomString(14);

        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = refreshToken,
            DeviceInfo = BrowserUtils.GetDeviceInfo(HttpContext.Request.Headers.UserAgent),
            ValidUntil = DateTime.UtcNow.AddDays(7)
        };

        var (secret, otpUrl) = UserUtils.GenerateTotp(user.Email);

        var response = new Recover2FaResponse
        {
            AccessToken = UserUtils.CreateAccessToken(user),
            RefreshToken = refreshToken,
            Secret = secret,
            OtpUrl = otpUrl
        };

        recoveryCode.WasUsed = true;
        user.TotpSecret = secret;
        await db.Sessions.AddAsync(session, ct);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation(
            "Successfully recovered 2FA for user {Email}. New session created from {DeviceInfo}",
            user.Email,
            session.DeviceInfo
        );
        return ApiResults.Ok(response);
    }
}