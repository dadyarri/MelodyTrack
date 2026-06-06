using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using UaDetector;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class LoginEndpoint(AppDbContext db, IUaDetector uaDetector, IAuditLogService auditLogService)
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
            Logger.LogWarning("auth.login.failed email {Email}", req.Email);
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
                    Logger.LogWarning("auth.login.failed_recovery_code email {Email}", req.Email);
                    return TypedResults.Unauthorized();
                }

                recoveryCode.WasUsed = true;
            }
            else if (!UserUtils.VerifyTotpCode(user.TotpSecret!, req.Otp))
            {
                Logger.LogWarning("auth.login.failed_otp email {Email}", req.Email);
                return TypedResults.Unauthorized();
            }
        }

        var refreshToken = UserUtils.GenerateRandomString(32);
        var deviceInfo = BrowserUtils.GetDeviceInfo(HttpContext.Request.Headers, uaDetector);

        await db.Sessions
            .Where(e => e.User.Id == user.Id && !e.WasRevoked && e.ValidUntil >= DateTime.UtcNow && e.DeviceInfo == deviceInfo)
            .ExecuteUpdateAsync(setters => setters.SetProperty(e => e.WasRevoked, true), ct);

        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = UserUtils.HashOpaqueToken(refreshToken),
            DeviceInfo = deviceInfo,
            ValidUntil = DateTime.UtcNow.AddDays(7)
        };

        await db.Sessions.AddAsync(session, ct);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("auth.login.succeeded user {Email} device {DeviceInfo}", user.Email, session.DeviceInfo);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "login_succeeded",
            EntityType = "session",
            EntityId = session.Id.ToString(),
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            ActorDisplayName = $"{user.LastName} {user.FirstName}".Trim(),
            Details = $"Устройство: {session.DeviceInfo}"
        }, ct);
        var response = new LoginResponse
        {
            AccessToken = UserUtils.CreateAccessToken(user, session.Id),
            RefreshToken = refreshToken,
            FirstName = user.FirstName,
            LastName = user.LastName
        };

        return TypedResults.Ok(response);
    }
}
