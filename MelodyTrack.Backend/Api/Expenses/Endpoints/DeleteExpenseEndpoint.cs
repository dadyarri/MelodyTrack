using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

public class DeleteExpenseEndpoint(AppDbContext db, IAuditLogService auditLogService) : Ep.Req<GetEntityRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Delete("/expenses/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Attempting to delete expense with ID: {ExpenseId}", req.Id);
        var expense = await db.Expenses
            .AsNoTracking()
            .Where(e => e.Id == req.Id)
            .Select(e => new { e.Id, e.Description, e.Amount, e.Date, CategoryName = e.Category != null ? e.Category.Name : null })
            .FirstOrDefaultAsync(ct);

        if (expense is null)
        {
            Logger.LogInformation("Expense with ID {ExpenseId} was already deleted or not found", req.Id);
            return TypedResults.NoContent();
        }

        await db.Expenses.Where(e => e.Id == req.Id).ExecuteDeleteAsync(ct);

        Logger.LogInformation("Successfully deleted expense with ID: {ExpenseId}", req.Id);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "expenses",
            Action = "expense_deleted",
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
