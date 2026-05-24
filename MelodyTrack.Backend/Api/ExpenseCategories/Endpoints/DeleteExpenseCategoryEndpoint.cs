using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ExpenseCategories.Endpoints;

public class DeleteExpenseCategoryEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<GetEntityRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Delete("/expense-categories/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult>> ExecuteAsync(
        GetEntityRequest req,
        CancellationToken ct)
    {
        var category = await db.ExpenseCategories
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == req.Id, ct);

        if (category is null)
        {
            return TypedResults.NoContent();
        }

        await db.Expenses
            .Where(e => e.CategoryId == req.Id)
            .ExecuteUpdateAsync(updates => updates.SetProperty(expense => expense.CategoryId, _ => null), ct);

        await db.ExpenseCategories
            .Where(e => e.Id == req.Id)
            .ExecuteDeleteAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "expenses",
            Action = "expense_category_deleted",
            EntityType = "expense_category",
            EntityId = category.Id.ToString(),
            Details = AuditDetailsFormatter.DescribeContext("Категория расхода", category.Name)
        }, ct);

        return TypedResults.NoContent();
    }
}
