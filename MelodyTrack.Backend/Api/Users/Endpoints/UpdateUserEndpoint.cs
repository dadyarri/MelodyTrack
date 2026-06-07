using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Users.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Users.Endpoints;

public class UpdateUserEndpoint(AppDbContext db, IEntityFreshnessService entityFreshnessService, IAuditLogService auditLogService)
    : Ep.Req<UpdateUserRequest>.Res<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Put("/users/{id}");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(
        UpdateUserRequest req,
        CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
        if (login is null)
        {
            return TypedResults.Unauthorized();
        }

        var currentUser = await db.Users
            .Include(user => user.Role)
            .WhereEmailMatches(login)
            .FirstOrDefaultAsync(ct);

        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (currentUser.Id != req.Id && !currentUser.Role.RoleName.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var user = await db.Users
            .Include(item => item.Role)
            .FirstOrDefaultAsync(item => item.Id == req.Id, ct);

        if (user is null)
        {
            AddError(item => item.Id, "Пользователь не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        if (user.Role.RoleName.IsSuperuser() && !currentUser.Role.RoleName.IsSuperuser())
        {
            return TypedResults.Forbid();
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "user",
            user.Id,
            req.ExpectedActivityId,
            "Пользователь был изменен другим пользователем. Обновите данные и повторите сохранение.",
            ct);

        if (conflict is not null && !IsNoOp(user, req))
        {
            return TypedResults.Conflict(conflict);
        }

        var beforeFirstName = user.FirstName;
        var beforeLastName = user.LastName;
        var beforePhone = user.Phone;
        var beforeTelegram = user.Telegram;
        var beforeVk = user.Vk;

        user.FirstName = req.FirstName;
        user.LastName = req.LastName;
        user.Phone = req.Phone;
        user.Telegram = req.Telegram;
        user.Vk = req.Vk;

        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "users",
            Action = "user_updated",
            EntityType = "user",
            EntityId = user.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Пользователь", $"{user.LastName} {user.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeChange("Имя", beforeFirstName, user.FirstName),
                AuditDetailsFormatter.DescribeChange("Фамилия", beforeLastName, user.LastName),
                AuditDetailsFormatter.DescribeChange("Телефон", beforePhone, user.Phone),
                AuditDetailsFormatter.DescribeChange("Telegram", beforeTelegram, user.Telegram),
                AuditDetailsFormatter.DescribeChange("VK", beforeVk, user.Vk)
            )
        }, ct);

        return TypedResults.NoContent();
    }

    private static bool IsNoOp(Data.Models.User user, UpdateUserRequest req)
    {
        return req.FirstName == user.FirstName
               && req.LastName == user.LastName
               && req.Phone == user.Phone
               && req.Telegram == user.Telegram
               && req.Vk == user.Vk;
    }
}
