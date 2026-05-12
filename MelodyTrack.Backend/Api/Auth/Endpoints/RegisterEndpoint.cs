using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class RegisterEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<RegisterRequest>.Res<Results<Created<RegisterResponse>, ProblemDetails>>
{
    public override void Configure()
    {
        Post("/auth/register");
        AllowAnonymous();
    }

    public override async Task<Results<Created<RegisterResponse>, ProblemDetails>> ExecuteAsync(RegisterRequest req,
        CancellationToken ct)
    {
        Logger.LogDebug("Validating invite code {InviteCode}", req.InviteCode);

        if (!Ulid.TryParse(req.InviteCode, out var code))
        {
            Logger.LogWarning("Invalid invite code {InviteCode}", req.InviteCode);
            AddError(r => r.InviteCode, "Ссылка приглашения недействительна. Используйте новую ссылку от администратора.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status403Forbidden);
        }

        var inviteCode = await db.InviteCodes
            .Include(inviteCode => inviteCode.Role)
            .FirstOrDefaultAsync(e =>
                e.Code == code && !e.WasUsed && e.ValidUntil >= DateTime.UtcNow, ct);

        if (inviteCode == null)
        {
            Logger.LogWarning("Invalid, used or expired invite code {InviteCode} provided", req.InviteCode);
            AddError(r => r.InviteCode, "Ссылка приглашения уже использована или просрочена. Попросите администратора создать новую.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status403Forbidden);
        }

        var email = (string.IsNullOrEmpty(inviteCode.Email) ? req.Email : inviteCode.Email).ToLowerInvariant();

        var hasUser = await db.Users.AnyAsync(u => u.Email == email, ct);

        if (hasUser)
        {
            Logger.LogWarning("Attempt to register with existing email {Email}", email);
            AddError(r => r.Email, "Пользователь с таким email уже зарегистрирован. Войдите в существующий аккаунт или попросите новую ссылку.");
            return ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status403Forbidden);
        }

        UserUtils.HashPassword(req.Password, out var hash);

        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = email.ToLowerInvariant(),
            FirstName = req.FirstName,
            LastName = req.LastName,
            Role = inviteCode.Role,
            Password = hash
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

        Logger.LogInformation(
            "auth.invite_accepted user {Email} role {Role} twoFactorRequired {TwoFactorRequired}",
            email,
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
