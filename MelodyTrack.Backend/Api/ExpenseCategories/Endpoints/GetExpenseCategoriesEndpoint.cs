using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.ExpenseCategories.Responses;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ExpenseCategories.Endpoints;

public class GetExpenseCategoriesEndpoint(AppDbContext db)
    : Ep.NoReq.Res<Results<Ok<GetExpenseCategoriesResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/expense-categories");
    }

    public override async Task<Results<Ok<GetExpenseCategoriesResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var categories = await db.ExpenseCategories
            .AsNoTracking()
            .OrderBy(e => e.Name)
            .Select(e => new ReferenceBookItemDto
            {
                Id = e.Id,
                Name = e.Name
            })
            .ToListAsync(ct);

        return TypedResults.Ok(new GetExpenseCategoriesResponse
        {
            Categories = categories
        });
    }
}
