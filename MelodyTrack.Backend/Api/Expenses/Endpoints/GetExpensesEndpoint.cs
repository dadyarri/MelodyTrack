using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Expenses.Requests;
using MelodyTrack.Backend.Api.Expenses.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

public class GetExpensesEndpoint(AppDbContext db) : Ep.Req<GetExpensesPaginatedRequest>.Res<Results<Ok<GetExpensesResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/expenses");
    }

    public override async Task<Results<Ok<GetExpensesResponse>, UnauthorizedHttpResult>> ExecuteAsync(GetExpensesPaginatedRequest req, CancellationToken ct)
    {
        var expensesQuery = db.Expenses
            .AsNoTracking()
            .ApplyDateRangeFilter(e => e.Date, req.Start, req.End);

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var pattern = $"%{req.Search.Trim().ToLower()}%";
            expensesQuery = expensesQuery.Where(e =>
                EF.Functions.ILike(e.Description, pattern)
                || (e.Category != null && EF.Functions.ILike(e.Category.Name, pattern)));
        }

        var totalCount = await expensesQuery.CountAsync(ct);
        var totalAmount = await expensesQuery.SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        var firstExpenseAtUtc = await expensesQuery
            .OrderBy(e => e.Date)
            .Select(e => (DateTime?)e.Date)
            .FirstOrDefaultAsync(ct);
        var lastExpenseAtUtc = await expensesQuery
            .OrderByDescending(e => e.Date)
            .Select(e => (DateTime?)e.Date)
            .FirstOrDefaultAsync(ct);

        var expenses = await expensesQuery
            .OrderByDescending(e => e.Date)
            .ApplyPagination(req)
            .Select(e => new ExpenseDto
            {
                Id = e.Id,
                Description = e.Description,
                Amount = e.Amount,
                Date = e.Date,
                CategoryId = e.CategoryId,
                CategoryName = e.Category != null ? e.Category.Name : null
            })
            .ToListAsync(ct);

        var response = PaginatedResponse.Create(expenses, totalCount, req);

        return TypedResults.Ok(new GetExpensesResponse
        {
            Data = response.Data,
            Info = response.Info,
            Summary = new MoneyListSummaryDto
            {
                TotalAmount = totalAmount,
                ItemsCount = (int)totalCount,
                FirstItemAtUtc = firstExpenseAtUtc,
                LastItemAtUtc = lastExpenseAtUtc
            }
        });
    }
}
