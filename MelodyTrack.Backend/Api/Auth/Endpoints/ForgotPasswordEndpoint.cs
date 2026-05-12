using FastEndpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Auth.Endpoints;

public class ForgotPasswordEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<ForgotPasswordRequest>.Res<Ok<ForgotPasswordResponse>>
{
    public override void Configure()
    {
        Post("/auth/forgotPassword");
        AllowAnonymous();
    }

    public override async Task<Ok<ForgotPasswordResponse>> ExecuteAsync(ForgotPasswordRequest req, CancellationToken ct)
    {
        var email = req.Email.ToLowerInvariant();
        var token = UserUtils.GenerateRandomString(14);
        var appDomain = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_APP_DOMAIN");
        var resetPageUrl = $"{appDomain}/restore?code={token}";
        var restorationRequest = new PasswordRestorationRequest
        {
            Id = Ulid.NewUlid(),
            Email = email,
            Token = token,
            ValidUntil = DateTime.UtcNow.AddHours(2)
        };

        Logger.LogInformation("auth.password_reset.requested email {Email} resetUrl {Url}", email, resetPageUrl);
        await db.PasswordRestorationRequests.AddAsync(restorationRequest, ct);
        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "password_reset_requested",
            EntityType = "password_reset",
            EntityId = restorationRequest.Id.ToString(),
            ActorEmail = email,
            Details = "Создан запрос на восстановление пароля"
        }, ct);

        return TypedResults.Ok(new ForgotPasswordResponse
        {
            Message = "Если аккаунт найден, новая ссылка для восстановления уже готова. Обратитесь к администратору."
        });
    }
}
