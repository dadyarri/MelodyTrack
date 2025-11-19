using FastEndpoints;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Expenses.Requests;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

public class CreateExpenseEndpoint(AppDbContext db) : Ep.Req<CreateExpenseRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/expenses");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult>> ExecuteAsync(CreateExpenseRequest req, CancellationToken ct)
    {
        var expense = new Expense
        {
            Id = Ulid.NewUlid(),
            Amount = req.Amount,
            Date = DateTime.UtcNow,
            Description = req.Description
        };

        await db.Expenses.AddAsync(expense, ct);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("Created new expense: {Description} with amount {Amount}", expense.Description, expense.Amount);

        return TypedResults.Created($"/expenses/{expense.Id}", new CreateEntityResponse
        {
            Id = expense.Id
        });
    }
}