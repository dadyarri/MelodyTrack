using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.ExpenseCategories.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ExpenseCategories.Endpoints;

public class GetExpenseCategoriesEndpoint(AppDbContext db, IRecordActivityService recordActivityService)
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

        var latestActivities = await recordActivityService.GetLatestActivitiesAsync(
            "expense_category",
            categories.Select(category => category.Id.ToString()).ToArray(),
            ct);

        foreach (var category in categories)
        {
            category.LastActivity = latestActivities.GetValueOrDefault(category.Id.ToString());
        }

        return TypedResults.Ok(new GetExpenseCategoriesResponse
        {
            Categories = categories
        });
    }
}
