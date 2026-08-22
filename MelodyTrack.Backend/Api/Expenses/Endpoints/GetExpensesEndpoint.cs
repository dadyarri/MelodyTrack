using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Expenses.Requests;
using MelodyTrack.Backend.Api.Expenses.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/expenses")]
public sealed class GetExpensesEndpoint
{

    public static async Task<Results<Ok<GetExpensesResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        [AsParameters] GetExpensesPaginatedRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IRecordActivityService recordActivityService,
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

        var latestActivities = await recordActivityService.GetLatestActivitiesAsync(
            "expense",
            expenses.Select(expense => expense.Id.ToString()).ToArray(),
            ct);

        foreach (var expense in expenses)
        {
            expense.LastActivity = latestActivities.GetValueOrDefault(expense.Id.ToString());
        }

        var response = PaginatedResponse.Create(expenses, totalCount, req);

        return TypedResults.Ok(new GetExpensesResponse
        {
            Items = response.Items,
            Page = response.Page,
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
