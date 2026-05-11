using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Expenses.Responses;

public class GetExpensesResponse : PaginatedResponse<Expense>
{
    public required ExpensesSummaryDto Summary { get; set; }
}

public class ExpensesSummaryDto
{
    public decimal TotalAmount { get; set; }
    public int ItemsCount { get; set; }
    public DateTime? FirstExpenseAtUtc { get; set; }
    public DateTime? LastExpenseAtUtc { get; set; }
}
