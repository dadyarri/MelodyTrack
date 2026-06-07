using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UaDetector;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class Recover2FaEndpoint(AppDbContext db, IUaDetector uaDetector)
    : Ep.Req<Recover2FaRequest>.Res<Results<Ok<Recover2FaResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/2fa/recover");
        AllowAnonymous();
        Throttle(10, 300);
    }

    public override async Task<Results<Ok<Recover2FaResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(
        Recover2FaRequest req,
        CancellationToken ct)
    {
        var user = await db.Users
            .Where(e => e.Email == req.Email.ToLowerInvariant())
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            Logger.LogWarning("2FA recovery attempt for non-existent user with email {Email}", req.Email);
            return TypedResults.Unauthorized();
        }

        var recoveryCode = await db.RecoveryCodes
            .Where(e => e.User.Id == user.Id && e.Code == req.RecoveryCode && !e.WasUsed)
            .FirstOrDefaultAsync(ct);

        if (recoveryCode is null)
        {
            Logger.LogWarning("2FA recovery attempt with invalid or used recovery code for user {Email}", req.Email);
            return TypedResults.Forbid();
        }

        await db.Sessions
            .Where(e => e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        await db.RecoveryCodes
            .Where(e => e.User.Id == user.Id && !e.WasUsed && e.Id != recoveryCode.Id)
            .ExecuteDeleteAsync(ct);

        var refreshToken = UserUtils.GenerateRandomString(32);

        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = UserUtils.HashOpaqueToken(refreshToken),
            DeviceInfo = BrowserUtils.GetDeviceInfo(HttpContext.Request.Headers, uaDetector),
            ValidUntil = DateTime.UtcNow.AddDays(7)
        };

        var (secret, otpUrl) = UserUtils.GenerateTotp(user.Email);
        var recoveryCodes = UserUtils.GenerateRecoveryCodes().ToList();

        var response = new Recover2FaResponse
        {
            AccessToken = UserUtils.CreateAccessToken(user, session.Id),
            RefreshToken = refreshToken,
            Secret = secret,
            OtpUrl = otpUrl,
            AllCodes = recoveryCodes.Select(code => new RecoveryCodeDto
            {
                Code = code,
                WasUsed = false
            }).ToList()
        };

        recoveryCode.WasUsed = true;
        user.TotpSecret = secret;
        await db.RecoveryCodes.AddRangeAsync(recoveryCodes.Select(code => new RecoveryCode
        {
            Id = Ulid.NewUlid(),
            Code = code,
            User = user
        }), ct);
        await db.Sessions.AddAsync(session, ct);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation(
            "Successfully recovered 2FA for user {Email}. New session created from {DeviceInfo}",
            user.Email,
            session.DeviceInfo
        );
        return TypedResults.Ok(response);
    }
}
