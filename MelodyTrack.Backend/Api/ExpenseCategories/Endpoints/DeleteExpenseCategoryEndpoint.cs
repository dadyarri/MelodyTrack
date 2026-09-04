using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ExpenseCategories.Endpoints;

[ApiEndpoint(ApiMethod.Delete, "/expense-categories/{id}")]
public sealed class DeleteExpenseCategoryEndpoint
{

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<NoContent, NotFound<ApiProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<StaleEntityConflictResponse>>> HandleAsync(
        [AsParameters] GetEntityRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IEntityFreshnessService entityFreshnessService,
        CancellationToken ct
    )
    {
        var currentUserRole = (await currentUserAccessor.GetAsync(ct))?.Role.RoleName;
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var category = await db.ExpenseCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == req.Id, ct);

        if (category is null)
        {
            return TypedResults.NoContent();
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "expense_category",
            category.Id,
            req.ExpectedActivityId,
            "Категория расхода была изменена другим пользователем. Проверьте последние изменения перед удалением.",
            ct);

        if (conflict is not null)
        {
            return TypedResults.Conflict(conflict);
        }

        await db.Expenses
            .Where(e => e.CategoryId == req.Id)
            .ExecuteUpdateAsync(updates => updates.SetProperty(expense => expense.CategoryId, _ => null), ct);

        await db.ExpenseCategories
            .Where(e => e.Id == req.Id)
            .ExecuteDeleteAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.ExpenseCategoryDeleted,
            EntityType = "expense_category",
            EntityId = category.Id.ToString(),
            Details = AuditDetailsFormatter.DescribeContext("Категория расхода", category.Name)
        }, ct);

        return TypedResults.NoContent();
    }
}
