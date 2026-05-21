using MelodyTrack.Backend.Api.Common.Responses;

namespace MelodyTrack.Backend.Api.Expenses.Responses;

public class GetExpensesResponse : PaginatedResponse<ExpenseDto>
{
    public required MoneyListSummaryDto Summary { get; set; }
}
