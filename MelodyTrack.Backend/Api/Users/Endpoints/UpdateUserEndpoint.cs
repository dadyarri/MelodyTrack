using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Users.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Users.Endpoints;

[ApiEndpoint(ApiMethod.Patch, "/users/{id}")]
public sealed class UpdateUserEndpoint
{

    public static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, Conflict<StaleEntityConflictResponse>>> HandleAsync(
        UpdateUserRequest req,
        Ulid id,
        AppDbContext db,
        IEntityFreshnessService entityFreshnessService,
        IAuditLogService auditLogService,
        ICurrentUserAccessor currentUserAccessor,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        req.Id = id;
        var currentUser = await currentUserAccessor.GetAsync(ct);
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
            validationErrors.Add(nameof(req.Id), "Пользователь не найден");
            return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
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
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.UserUpdated,
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
