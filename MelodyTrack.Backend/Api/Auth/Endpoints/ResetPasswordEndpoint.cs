using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class ResetPasswordEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<ResetPasswordRequest>.Res<Results<NoContent, ProblemDetails>>
{
    public override void Configure()
    {
        Post("/auth/resetPassword");
        AllowAnonymous();
        Throttle(10, 600);
    }

    public override async Task<Results<NoContent, ProblemDetails>> ExecuteAsync(
        ResetPasswordRequest req,
        CancellationToken ct)
    {
        var tokenHash = UserUtils.HashOpaqueToken(req.Token);
        var restoreCode = await db.PasswordRestorationRequests
            .Where(e => !e.WasUsed && e.Token == tokenHash)
            .FirstOrDefaultAsync(ct);

        if (restoreCode is null || restoreCode.ValidUntil < DateTime.UtcNow)
        {
            Logger.LogWarning("Password reset attempt with invalid, used or expired token");
            AddError(r => r.Token, "Ссылка восстановления больше не действует. Запросите новую ссылку.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status403Forbidden);
        }

        var user = await db.Users
            .Where(e => e.Email == restoreCode.Email)
            .Include(e => e.Role)
            .FirstOrDefaultAsync(ct);

        if (user is null || (user.Role.RoleName.IsAnyAdmin() || user.TotpSecret is not null) &&
            req.Otp is null && string.IsNullOrWhiteSpace(req.RecoveryCode))
        {
            Logger.LogWarning("Password reset attempt for non-existent user or missing 2FA code for user {Email}", restoreCode.Email);
            AddError(r => r.Otp, "Для этого аккаунта нужен код 2FA или код восстановления.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status403Forbidden);
        }

        if (user.Role.RoleName.IsAnyAdmin() || user.TotpSecret is not null)
        {
            if (!string.IsNullOrWhiteSpace(req.RecoveryCode))
            {
                var recoveryCode = await db.RecoveryCodes
                    .FirstOrDefaultAsync(e => e.User.Id == user.Id && e.Code == req.RecoveryCode && !e.WasUsed, ct);

                if (recoveryCode is null)
                {
                    Logger.LogWarning("Invalid recovery code provided during password reset for user {Email}", user.Email);
                    AddError(r => r.RecoveryCode, "Код восстановления неверный или уже использован.");
                    return ApiErrorResponseFactory.CreateValidationProblemDetails(
                        ValidationFailures,
                        HttpContext,
                        StatusCodes.Status401Unauthorized);
                }

                recoveryCode.WasUsed = true;
            }
            else if (!UserUtils.VerifyTotpCode(user.TotpSecret!, req.Otp))
            {
                Logger.LogWarning("Invalid 2FA code provided during password reset for user {Email}", user.Email);
                AddError(r => r.Otp, "Код 2FA неверный. Проверьте код из приложения-аутентификатора и попробуйте снова.");
                return ApiErrorResponseFactory.CreateValidationProblemDetails(
                    ValidationFailures,
                    HttpContext,
                    StatusCodes.Status401Unauthorized);
            }
        }

        UserUtils.HashPassword(req.NewPassword, out var hash);
        user.Password = hash;
        restoreCode.WasUsed = true;
        await db.SaveChangesAsync(ct);

        await db.Sessions.Where(e => e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);

        Logger.LogInformation("auth.password_reset.completed user {Email}", user.Email);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "password_reset_completed",
            EntityType = "user",
            EntityId = user.Id.ToString(),
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            ActorDisplayName = $"{user.LastName} {user.FirstName}".Trim(),
            Details = "Пароль восстановлен по ссылке"
        }, ct);
        return TypedResults.NoContent();
    }
}
