using FastEndpoints;
using Backend.Data;
using Backend.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Expenses.Endpoints;

public class GetCurrentMonthExpensesEndpoint(AppDbContext dbContext)
    : EndpointWithoutRequest<CurrentMonthExpensesResponse>
{
    public override void Configure()
    {
        Get("/api/expenses/current-month");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var today = DateTime.UtcNow;
        var firstDayOfMonth = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var expenses = await dbContext.Expenses
            .Where(e => e.Date >= firstDayOfMonth)
            .ToListAsync(ct);

        var response = new CurrentMonthExpensesResponse
        {
            TotalAmount = expenses.Sum(e => e.Amount),
            Count = expenses.Count,
            Expenses = expenses
        };

        await Send.OkAsync(response, ct);
    }
}

public class CurrentMonthExpensesResponse
{
    public decimal TotalAmount { get; set; }
    public int Count { get; set; }
    public List<Expense> Expenses { get; set; } = new();
} 