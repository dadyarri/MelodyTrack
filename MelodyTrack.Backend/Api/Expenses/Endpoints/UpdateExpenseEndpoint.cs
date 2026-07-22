using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Expenses.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

public class UpdateExpenseEndpoint(AppDbContext db, IAuditLogService auditLogService, IEntityFreshnessService entityFreshnessService)
    : Ep.Req<UpdateExpenseRequest>.Res<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Put("/expenses/{id}");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(
        UpdateExpenseRequest req,
        CancellationToken ct)
    {
        var currentUserRole = await EndpointAuthUtils.GetCurrentUserRoleAsync(User, db, ct);
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsSuperuser())
        {
            return TypedResults.Forbid();
        }

        var expense = await db.Expenses
            .Include(item => item.Category)
            .FirstOrDefaultAsync(item => item.Id == req.Id, ct);

        if (expense is null)
        {
            AddError(item => item.Id, "Расход не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        string? categoryName = null;
        if (req.CategoryId is not null)
        {
            categoryName = await db.ExpenseCategories
                .Where(item => item.Id == req.CategoryId.Value)
                .Select(item => item.Name)
                .FirstOrDefaultAsync(ct);

            if (categoryName is null)
            {
                AddError(item => item.CategoryId, "Категория расхода не найдена");
                return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
            }
        }

        var date = req.Date.ToUniversalTime();
        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "expense",
            expense.Id,
            req.ExpectedActivityId,
            "Расход был изменен другим пользователем. Обновите данные или повторите сохранение поверх новой версии.",
            ct);

        if (conflict is not null && !IsNoOp(expense, req, date))
        {
            return TypedResults.Conflict(conflict);
        }

        var beforeDescription = expense.Description;
        var beforeAmount = expense.Amount;
        var beforeDate = expense.Date;
        var beforeCategoryName = expense.Category?.Name;

        expense.Description = req.Description;
        expense.Amount = req.Amount;
        expense.Date = date;
        expense.CategoryId = req.CategoryId;

        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "expenses",
            Action = "expense_updated",
            EntityType = "expense",
            EntityId = expense.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeChange("Описание", beforeDescription, expense.Description),
                AuditDetailsFormatter.DescribeChange("Сумма", beforeAmount.ToString("0.##"), expense.Amount.ToString("0.##")),
                AuditDetailsFormatter.DescribeChange("Категория", beforeCategoryName, categoryName),
                AuditDetailsFormatter.DescribeChange("Дата", beforeDate, expense.Date)
            )
        }, ct);

        return TypedResults.NoContent();
    }

    private static bool IsNoOp(Data.Models.Expense expense, UpdateExpenseRequest req, DateTime date)
    {
        return expense.Description == req.Description
               && expense.Amount == req.Amount
               && expense.Date == date
               && expense.CategoryId == req.CategoryId;
    }
}
