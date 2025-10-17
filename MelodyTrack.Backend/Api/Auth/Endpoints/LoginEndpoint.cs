using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using Serilog;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class LoginEndpoint(AppDbContext db)
    : Ep.Req<LoginRequest>.Res<Results<Ok<LoginResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/auth/login");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>> ExecuteAsync(LoginRequest req,
        CancellationToken ct)
    {
        var user = await db.Users
            .Include(e => e.Role)
            .FirstOrDefaultAsync(e => e.Email == req.Email, ct);

        if (user is null || UserUtils.IsValidPassword(user.Password, req.Password) ||
            (user.Role.RoleName.IsAnyAdmin() && req.Otp is null))
        {
            return TypedResults.Unauthorized();
        }

        if (user.Role.RoleName.IsAnyAdmin() || user.TotpSecret is not null)
        {
            var secretKey = Base32Encoding.ToBytes(user.TotpSecret);
            var totp = new Totp(secretKey, mode: OtpHashMode.Sha512);
            if (!totp.VerifyTotp(req.Otp, out _, new VerificationWindow(1, 1)))
            {
                return TypedResults.Unauthorized();
            }
        }

        await db.Sessions.Where(e => e.User == user)
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

        await db.Sessions.AddAsync(session, ct);
        await db.SaveChangesAsync(ct);

        var response = new LoginResponse
        {
            AccessToken = UserUtils.CreateAccessToken(user),
            RefreshToken = refreshToken,
            FirstName = user.FirstName,
            LastName = user.LastName
        };

        return TypedResults.Ok(response);
    }
}