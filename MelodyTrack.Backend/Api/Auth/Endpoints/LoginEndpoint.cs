using FastEndpoints;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Common.Api.Auth.Requests;
using MelodyTrack.Common.Api.Auth.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Enums;
using MelodyTrack.Common.Data.Models;
using Microsoft.EntityFrameworkCore;
using OtpNet;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class LoginEndpoint(AppDbContext db)
    : Ep.Req<LoginRequest>.Res<IResult>
{
    public override void Configure()
    {
        Post("/auth/login");
        AllowAnonymous();
    }

    public override async Task<IResult> ExecuteAsync(LoginRequest req,
        CancellationToken ct)
    {
        Logger.LogDebug("Attempting to authenticate user with email {Email}", req.Email);

        var user = await db.Users
            .Include(e => e.Role)
            .FirstOrDefaultAsync(e => e.Email == req.Email.ToLowerInvariant(), ct);

        if (user is null || !UserUtils.IsValidPassword(user.Password, req.Password) ||
            user.Role.RoleName.IsAnyAdmin() && req.Otp is null)
        {
            Logger.LogWarning("Failed login attempt for email {Email}", req.Email);
            return ApiResults.Unauthorized("Неправильный адрес почты или пароль");
        }

        if (user.Role.RoleName.IsAnyAdmin() || user.TotpSecret is not null)
        {
            var secretKey = Base32Encoding.ToBytes(user.TotpSecret);
            var totp = new Totp(secretKey, mode: OtpHashMode.Sha512);
            if (!totp.VerifyTotp(req.Otp, out _, new VerificationWindow(1, 1)))
            {
                Logger.LogWarning("Invalid 2FA code provided for user {Email}", req.Email);
                return ApiResults.Unauthorized("Неправильный одноразовый код");
            }
            Logger.LogDebug("2FA verification successful for user {Email}", req.Email);
        }

        var refreshToken = UserUtils.GenerateRandomString(14);

        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = refreshToken,
            DeviceInfo = BrowserUtils.GetDeviceInfo(HttpContext.Request.Headers.UserAgent),
            ValidUntil = DateTime.UtcNow.AddDays(7)
        };

        await db.Sessions.AddAsync(session, ct);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("User {Email} successfully logged in from {DeviceInfo}", user.Email, session.DeviceInfo);

        var response = new LoginResponse
        {
            AccessToken = UserUtils.CreateAccessToken(user),
            RefreshToken = refreshToken,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Role = user.Role.RoleName
        };

        return ApiResults.Ok(response, "Успешный вход");
    }
}