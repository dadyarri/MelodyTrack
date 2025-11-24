using FastEndpoints;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Expenses.Requests;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Models;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

public class CreateExpenseEndpoint(AppDbContext db) : Ep.Req<CreateExpenseRequest>.Res<IResult>
{
    public override void Configure()
    {
        Post("/expenses");
    }

    public override async Task<IResult> ExecuteAsync(CreateExpenseRequest req, CancellationToken ct)
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

        return ApiResults.Created($"/expenses/{expense.Id}", new CreateEntityResponse
        {
            Id = expense.Id
        });
    }
}