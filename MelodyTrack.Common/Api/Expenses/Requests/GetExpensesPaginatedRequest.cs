using MelodyTrack.Common.Api.Common.Requests;

namespace MelodyTrack.Common.Api.Expenses.Requests;

public class GetExpensesPaginatedRequest : PaginatedRequest
{
    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
}