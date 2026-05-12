using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Expenses.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

public class CreateExpenseEndpoint(AppDbContext db, IAuditLogService auditLogService) : Ep.Req<CreateExpenseRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult>>
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
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "expenses",
            Action = "expense_created",
            EntityType = "expense",
            EntityId = expense.Id.ToString(),
            Details = $"{expense.Description}, сумма {expense.Amount}"
        }, ct);

        return TypedResults.Created($"/expenses/{expense.Id}", new CreateEntityResponse
        {
            Id = expense.Id
        });
    }
}
