using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MelodyTrack.Data.Security;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/auth/password-reset")]
public sealed class ResetPasswordEndpoint
{

        [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.ResetPassword)]
    public static async Task<Results<NoContent, ApiProblemDetails>> HandleAsync(
        ResetPasswordRequest req,
        AppDbContext db,
        IAuditLogService auditLogService,
        RefreshSessionCookieService refreshCookieService,
        CredentialHasher credentialHasher,
        TimeProvider timeProvider,
        ILogger<ResetPasswordEndpoint> logger,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        var tokenHash = UserUtils.HashOpaqueToken(req.Token);
        var restoreCode = await db.PasswordRestorationRequests
            .Where(e => !e.WasUsed && e.Token == tokenHash)
            .FirstOrDefaultAsync(ct);

        if (restoreCode is null || restoreCode.ValidUntil < timeProvider.GetUtcNow().UtcDateTime)
        {
            logger.LogWarning("Password reset attempt with invalid, used or expired token");
            validationErrors.Add(nameof(req.Token), "Ссылка восстановления больше не действует. Запросите новую ссылку.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                validationErrors,
                httpContext,
                StatusCodes.Status403Forbidden);
        }

        var user = await db.Users
            .WhereEmailMatches(restoreCode.Email)
            .Include(e => e.Role)
            .FirstOrDefaultAsync(ct);

        if (user is null || (user.Role.RoleName.IsAnyAdmin() || user.TotpSecret is not null) &&
            req.Otp is null && string.IsNullOrWhiteSpace(req.RecoveryCode))
        {
            logger.LogWarning("Password reset attempt for non-existent user or missing 2FA code for {EmailRef}", UserUtils.DescribeEmailForLogs(restoreCode.Email));
            validationErrors.Add(nameof(req.Otp), "Для этого аккаунта нужен код 2FA или код восстановления.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                validationErrors,
                httpContext,
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
                    logger.LogWarning("Invalid recovery code provided during password reset for {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
                    validationErrors.Add(nameof(req.RecoveryCode), "Код восстановления неверный или уже использован.");
                    return ApiErrorResponseFactory.CreateValidationProblemDetails(
                        validationErrors,
                        httpContext,
                        StatusCodes.Status401Unauthorized);
                }

                recoveryCode.WasUsed = true;
            }
            else if (!UserUtils.VerifyTotpCode(user.TotpSecret!, req.Otp))
            {
                logger.LogWarning("Invalid 2FA code provided during password reset for {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
                validationErrors.Add(nameof(req.Otp), "Код 2FA неверный. Проверьте код из приложения-аутентификатора и попробуйте снова.");
                return ApiErrorResponseFactory.CreateValidationProblemDetails(
                    validationErrors,
                    httpContext,
                    StatusCodes.Status401Unauthorized);
            }
        }

        user.Password = credentialHasher.HashPassword(req.NewPassword);
        user.PasswordResetRequired = false;
        restoreCode.WasUsed = true;
        await db.SaveChangesAsync(ct);

        await db.Sessions.Where(e => e.User.Id == user.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(e => e.WasRevoked, true), ct);
        refreshCookieService.Clear(httpContext.Response);

        logger.LogInformation("auth.password_reset.completed {EmailRef}", UserUtils.DescribeEmailForLogs(user.Email));
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.PasswordResetCompleted,
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
