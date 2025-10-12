using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Expenses.Endpoints;

/// <summary>
///     Получить расходы в текущем месяце
/// </summary>
/// <param name="dbContext">БД</param>
public class GetCurrentMonthExpensesEndpoint(AppDbContext dbContext)
    : EndpointWithoutRequest<CurrentMonthExpensesResponse>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/api/expenses/current-month");
        AllowAnonymous();
    }

    /// <inheritdoc />
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

/// <summary>
///     Ответ на успешный запрос данных о расходах в текущем месяце
/// </summary>
public class CurrentMonthExpensesResponse
{
    /// <summary>
    ///     Общая сумма
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    ///     Количество
    /// </summary>
    public int Count { get; set; }

    /// <summary>
    ///     Расходы
    /// </summary>
    public List<Expense> Expenses { get; set; } = new();
}