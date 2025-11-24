using FastEndpoints;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Expenses.Requests;
using MelodyTrack.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

public class GetExpensesEndpoint(AppDbContext db) : Ep.Req<GetExpensesPaginatedRequest>.Res<IResult>
{
    public override void Configure()
    {
        Get("/expenses");
    }

    public override async Task<IResult> ExecuteAsync(GetExpensesPaginatedRequest req, CancellationToken ct)
    {
        var expenses = await db.Expenses
            .AsNoTracking()
            .ApplyDateRangeFilter(e => e.Date, req.Start, req.End)
            .ApplyPagination(req)
            .ToListAsync(ct);

        var totalCount = await db.Expenses.CountAsync(ct);

        return ApiResults.Ok(PaginatedResponse.Create(expenses, totalCount, req));
    }
}