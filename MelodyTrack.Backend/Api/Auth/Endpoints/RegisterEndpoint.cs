using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MelodyTrack.Data.Security;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/auth/register")]
public sealed class RegisterEndpoint
{

        [AllowAnonymous]
    [EnableRateLimiting(ApiRateLimitPolicies.Register)]
    public static async Task<Results<Created<RegisterResponse>, ApiProblemDetails>> HandleAsync(
        RegisterRequest req,
        AppDbContext db,
        IAuditLogService auditLogService,
        CredentialHasher credentialHasher,
        TimeProvider timeProvider,
        ILogger<RegisterEndpoint> logger,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        if (!Ulid.TryParse(req.InviteCode, out var code))
        {
            logger.LogWarning("Invalid invite code format");
            validationErrors.Add(nameof(req.InviteCode), "Ссылка приглашения недействительна. Используйте новую ссылку от администратора.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                validationErrors,
                httpContext,
                StatusCodes.Status403Forbidden);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var inviteCode = await db.InviteCodes
            .Include(inviteCode => inviteCode.Role)
            .FirstOrDefaultAsync(e =>
                e.Code == code && !e.WasUsed && e.ValidUntil >= nowUtc, ct);

        if (inviteCode == null)
        {
            logger.LogWarning("Invalid, used or expired invite code {InviteReference} provided", UserUtils.DescribeInviteCodeForLogs(code));
            validationErrors.Add(nameof(req.InviteCode), "Ссылка приглашения уже использована или просрочена. Попросите администратора создать новую.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                validationErrors,
                httpContext,
                StatusCodes.Status403Forbidden);
        }

        var email = UserUtils.NormalizeEmail(string.IsNullOrEmpty(inviteCode.Email) ? req.Email : inviteCode.Email);

        var hasUser = await db.Users.WhereEmailMatches(email).AnyAsync(ct);

        if (hasUser)
        {
            logger.LogWarning("Attempt to register with existing {EmailRef}", UserUtils.DescribeEmailForLogs(email));
            validationErrors.Add(nameof(req.Email), "Пользователь с таким email уже зарегистрирован. Войдите в существующий аккаунт или попросите новую ссылку.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                validationErrors,
                httpContext,
                StatusCodes.Status403Forbidden);
        }

        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = email,
            FirstName = req.FirstName,
            LastName = req.LastName,
            Role = inviteCode.Role,
            Password = credentialHasher.HashPassword(req.Password)
        };

        inviteCode.WasUsed = true;

        var isTotpRequired = inviteCode.Role.RoleName.IsAnyAdmin();
        RegisterResponse? response;
        if (isTotpRequired)
        {

            var (secret, otpUrl) = UserUtils.GenerateTotp(user.Email);

            user.TotpSecret = secret;
            response = new RegisterResponse
            {
                TotpRequired = isTotpRequired,
                Secret = secret,
                OtpUrl = otpUrl
            };
        }
        else
        {
            response = new RegisterResponse
            {
                TotpRequired = isTotpRequired
            };
        }

        await db.Users.AddAsync(user, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "auth.invite_accepted {EmailRef} role {Role} twoFactorRequired {TwoFactorRequired}",
            UserUtils.DescribeEmailForLogs(email),
            inviteCode.Role.RoleName,
            isTotpRequired);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "user_registered",
            EntityType = "user",
            EntityId = user.Id.ToString(),
            ActorUserId = user.Id,
            ActorEmail = user.Email,
            ActorDisplayName = $"{user.LastName} {user.FirstName}".Trim(),
            Details = $"Регистрация по приглашению, роль {inviteCode.Role.DisplayName}"
        }, ct);
        return TypedResults.Created("/auth/register", response);
    }
}
