using MelodyTrack.Backend.Api.Common.Requests;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Expenses.Requests;

public class GetExpensesPaginatedRequest : PaginatedRequest
{
    [FromQuery(Name = "search")]
    public string? Search { get; set; }

    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
}
