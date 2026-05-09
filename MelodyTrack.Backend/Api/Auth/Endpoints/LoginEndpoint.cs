using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UaDetector;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class LoginEndpoint(AppDbContext db, IUaDetector uaDetector)
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
        Logger.LogDebug("Attempting to authenticate user with email {Email}", req.Email);

        var user = await db.Users
            .Include(e => e.Role)
            .FirstOrDefaultAsync(e => e.Email == req.Email.ToLowerInvariant(), ct);

        if (user is null || !UserUtils.IsValidPassword(user.Password, req.Password) ||
            user.Role.RoleName.IsAnyAdmin() && req.Otp is null && string.IsNullOrWhiteSpace(req.RecoveryCode))
        {
            Logger.LogWarning("Failed login attempt for email {Email}", req.Email);
            return TypedResults.Unauthorized();
        }

        if (user.Role.RoleName.IsAnyAdmin() || user.TotpSecret is not null)
        {
            if (!string.IsNullOrWhiteSpace(req.RecoveryCode))
            {
                var recoveryCode = await db.RecoveryCodes
                    .FirstOrDefaultAsync(e => e.User.Id == user.Id && e.Code == req.RecoveryCode && !e.WasUsed, ct);

                if (recoveryCode is null)
                {
                    Logger.LogWarning("Invalid recovery code provided for user {Email}", req.Email);
                    return TypedResults.Unauthorized();
                }

                recoveryCode.WasUsed = true;
            }
            else if (!UserUtils.VerifyTotpCode(user.TotpSecret!, req.Otp))
            {
                Logger.LogWarning("Invalid 2FA code provided for user {Email}", req.Email);
                return TypedResults.Unauthorized();
            }

            Logger.LogDebug("2FA verification successful for user {Email}", req.Email);
        }

        var refreshToken = UserUtils.GenerateRandomString(14);

        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = refreshToken,
            DeviceInfo = BrowserUtils.GetDeviceInfo(HttpContext.Request.Headers, uaDetector),
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
            LastName = user.LastName
        };

        return TypedResults.Ok(response);
    }
}
