using Backend.Data;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Expenses.Endpoints;

/// <summary>
///     Удалить расход
/// </summary>
/// <param name="db">БД</param>
public class DeleteExpenseEndpoint(AppDbContext db)
    : Endpoint<EmptyRequest, Results<NoContent, NotFound, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Delete("/api/expenses/{id:long}");
    }

    /// <inheritdoc />
    public override async Task<Results<NoContent, NotFound, ProblemDetails>> ExecuteAsync(
        EmptyRequest req, CancellationToken ct)
    {
        var expenseId = Route<long>("id");

        var expense = await db.Expenses.Where(e => e.Id == expenseId).FirstOrDefaultAsync(ct);

        if (expense == null) return TypedResults.NotFound();

        db.Remove(expense);

        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }
}