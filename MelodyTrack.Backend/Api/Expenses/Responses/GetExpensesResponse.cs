using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Expenses.Responses;

public class GetExpensesResponse : PaginatedResponse<Expense>
{
    public required MoneyListSummaryDto Summary { get; set; }
}
