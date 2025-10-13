using Backend.Api.Base.Models;
using Backend.Api.Expenses.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Backend.Api.Expenses.Endpoints;

/// <summary>
///     Создать расход
/// </summary>
/// <param name="db">БД</param>
public class CreateExpenseEndpoint(AppDbContext db)
    : Endpoint<CreateExpenseRequest, Results<Ok<CreateEntityResponse>, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Post("/expenses");
    }

    /// <inheritdoc />
    public override async Task<Results<Ok<CreateEntityResponse>, ProblemDetails>> ExecuteAsync(CreateExpenseRequest req,
        CancellationToken ct)
    {
        var expense = new Expense
        {
            Description = req.Description,
            Amount = req.Amount,
            Date = DateTime.UtcNow
        };

        await db.Expenses.AddAsync(expense, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new CreateEntityResponse
        {
            Id = expense.Id
        });
    }
}