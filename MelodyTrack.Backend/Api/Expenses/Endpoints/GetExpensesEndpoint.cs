using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Expenses.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

public class GetExpensesEndpoint(AppDbContext db) : Ep.Req<GetExpensesPaginatedRequest>.Res<Results<Ok<PaginatedResponse<Expense>>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/expenses");
    }

    public override async Task<Results<Ok<PaginatedResponse<Expense>>, UnauthorizedHttpResult>> ExecuteAsync(GetExpensesPaginatedRequest req, CancellationToken ct)
    {
        var expenses = await db.Expenses
            .AsNoTracking()
            .ApplyDateRangeFilter(e => e.Date, req.Start, req.End)
            .ApplyPagination(req)
            .ToListAsync(ct);

        var totalCount = await db.Expenses.CountAsync(ct);

        return TypedResults.Ok(PaginatedResponse.Create(expenses, totalCount, req));
    }
}