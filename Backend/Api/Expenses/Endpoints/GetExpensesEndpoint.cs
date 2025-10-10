using Backend.Api.Base.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Expenses.Endpoints;

/// <summary>
/// Получить расходы
/// </summary>
/// <param name="db">БД</param>
public class GetExpensesEndpoint(AppDbContext db)
    : Endpoint<PaginationRequest, Results<Ok<PaginatedResponse<Expense>>, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/api/expenses");
    }

    /// <inheritdoc />
    public override async Task<Results<Ok<PaginatedResponse<Expense>>, ProblemDetails>> ExecuteAsync(
        PaginationRequest req, CancellationToken ct)
    {
        var skipped = req.PageSize * (req.Page - 1);
        var expenses = await db.Expenses
            .Skip(skipped)
            .Take(req.PageSize)
            .ToListAsync(ct);

        var expensesCount = await db.Expenses.CountAsync(ct);

        return TypedResults.Ok(PaginatedResponse<Expense>.Create(expenses, expensesCount, req.Page, req.PageSize,
            skipped));
    }
}