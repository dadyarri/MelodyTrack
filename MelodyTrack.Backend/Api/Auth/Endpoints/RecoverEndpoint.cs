using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class RecoverEndpoint(AppDbContext db)
    : Ep.Req<RecoverRequest>.Res<Results<Ok<RecoverResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/2fa/recover");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<RecoverResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(
        RecoverRequest req,
        CancellationToken ct)
    {
        var user = await db.Users
            .Where(e => e.Email == req.Email)
            .FirstOrDefaultAsync(ct);

        if (user is null)
        {
            return TypedResults.Unauthorized();
        }

        var recoveryCode = await db.RecoveryCodes
            .Where(e => e.Code == req.RecoveryCode && !e.WasUsed)
            .FirstOrDefaultAsync(ct);

        if (recoveryCode is null)
        {
            return TypedResults.Forbid();
        }

        await db.Sessions
            .Where(e => e.User == user)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        var refreshToken = UserUtils.GenerateRandomString(14);

        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = refreshToken,
            DeviceInfo = BrowserUtils.GetDeviceInfo(HttpContext.Request.Headers.UserAgent),
            ValidUntil = DateTime.UtcNow.AddDays(7),
        };

        var (secret, otpUrl) = UserUtils.GenerateTotp(user.Email);

        var response = new RecoverResponse
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

        return TypedResults.Ok(response);
    }
}