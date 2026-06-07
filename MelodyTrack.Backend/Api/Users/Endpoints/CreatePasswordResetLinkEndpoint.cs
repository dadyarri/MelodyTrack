using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Users.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Users.Endpoints;

public class CreatePasswordResetLinkEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<GetEntityRequest>.Res<Results<Created<CreatePasswordResetLinkResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>>>
{
    public override void Configure()
    {
        Post("/users/{id}/password-reset-link");
    }

    public override async Task<Results<Created<CreatePasswordResetLinkResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>>> ExecuteAsync(
        GetEntityRequest req,
        CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;

        if (login is null)
        {
            Logger.LogWarning("Password reset link creation attempt without valid email claim");
            return TypedResults.Unauthorized();
        }

        var caller = await db.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .WhereEmailMatches(login)
            .FirstOrDefaultAsync(ct);

        if (caller is null)
        {
            Logger.LogWarning("Password reset link creation attempt for non-existent caller {EmailRef}", UserUtils.DescribeEmailForLogs(login));
            return TypedResults.Unauthorized();
        }

        if (!caller.Role.RoleName.IsAnyAdmin())
        {
            Logger.LogWarning("Password reset link creation attempt without admin access by {EmailRef}", UserUtils.DescribeEmailForLogs(caller.Email));
            return TypedResults.Forbid();
        }

        var targetUser = await db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Id == req.Id, ct);

        if (targetUser is null)
        {
            AddError(r => r.Id, "Пользователь не найден");
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status404NotFound));
        }

        if (targetUser.Role.RoleName.IsSuperuser() && !caller.Role.RoleName.IsSuperuser())
        {
            Logger.LogWarning(
                "Admin {EmailRef} attempted to create a superuser password reset link without sufficient privileges",
                UserUtils.DescribeEmailForLogs(caller.Email));
            return TypedResults.Forbid();
        }

        await db.PasswordRestorationRequests
            .Where(request => request.Email == targetUser.Email && !request.WasUsed)
            .ExecuteUpdateAsync(setters => setters.SetProperty(request => request.WasUsed, true), ct);

        var token = UserUtils.GenerateRandomString(32);
        var restorationRequest = new PasswordRestorationRequest
        {
            Id = Ulid.NewUlid(),
            Email = targetUser.Email,
            Token = UserUtils.HashOpaqueToken(token),
            ValidUntil = DateTime.UtcNow.AddHours(2)
        };

        await db.PasswordRestorationRequests.AddAsync(restorationRequest, ct);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation(
            "auth.password_reset_link.created actor {ActorEmailRef} target {TargetEmailRef}",
            UserUtils.DescribeEmailForLogs(caller.Email),
            UserUtils.DescribeEmailForLogs(targetUser.Email));
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "auth",
            Action = "password_reset_link_created",
            EntityType = "password_reset",
            EntityId = restorationRequest.Id.ToString(),
            ActorUserId = caller.Id,
            ActorEmail = caller.Email,
            ActorDisplayName = $"{caller.LastName} {caller.FirstName}".Trim(),
            Details = $"Создана ссылка на восстановление пароля для {UserUtils.DescribeEmailForLogs(targetUser.Email)}"
        }, ct);

        return TypedResults.Created(
            $"/users/{req.Id}/password-reset-link",
            new CreatePasswordResetLinkResponse
            {
                Url = UserUtils.GetResetPasswordUrl(token)
            });
    }
}
