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

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

[ApiEndpoint(ApiMethod.Delete, "/expenses/{id}")]
public sealed class DeleteExpenseEndpoint
{

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, Conflict<StaleEntityConflictResponse>>> HandleAsync(
        [AsParameters] GetEntityRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IEntityFreshnessService entityFreshnessService,
        ILogger<DeleteExpenseEndpoint> logger,
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

        logger.LogDebug("Attempting to delete expense with ID: {ExpenseId}", req.Id);
        var expense = await db.Expenses
            .AsNoTracking()
            .Where(e => e.Id == req.Id)
            .Select(e => new { e.Id, e.Description, e.Amount, e.Date, CategoryName = e.Category != null ? e.Category.Name : null })
            .FirstOrDefaultAsync(ct);

        if (expense is null)
        {
            logger.LogInformation("Expense with ID {ExpenseId} was already deleted or not found", req.Id);
            return TypedResults.NoContent();
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "expense",
            expense.Id,
            req.ExpectedActivityId,
            "Расход был изменен другим пользователем. Проверьте последние изменения перед удалением.",
            ct);

        if (conflict is not null)
        {
            return TypedResults.Conflict(conflict);
        }

        await db.Expenses.Where(e => e.Id == req.Id).ExecuteDeleteAsync(ct);

        logger.LogInformation("Successfully deleted expense with ID: {ExpenseId}", req.Id);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.ExpenseDeleted,
            EntityType = "expense",
            EntityId = expense.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Описание", expense.Description),
                AuditDetailsFormatter.DescribeContext("Сумма", expense.Amount.ToString("0.##")),
                AuditDetailsFormatter.DescribeContext("Категория", expense.CategoryName),
                AuditDetailsFormatter.DescribeContext("Дата", expense.Date)
            )
        }, ct);
        return TypedResults.NoContent();
    }
}
