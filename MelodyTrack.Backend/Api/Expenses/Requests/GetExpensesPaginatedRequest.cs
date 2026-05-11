using MelodyTrack.Backend.Api.Common.Requests;
using FastEndpoints;

namespace MelodyTrack.Backend.Api.Expenses.Requests;

public class GetExpensesPaginatedRequest : PaginatedRequest
{
    [BindFrom("search")]
    public string? Search { get; set; }

    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
}
