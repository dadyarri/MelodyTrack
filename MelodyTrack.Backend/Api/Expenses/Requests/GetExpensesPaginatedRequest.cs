using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Common.Requests;

namespace MelodyTrack.Backend.Api.Expenses.Requests;

public class GetExpensesPaginatedRequest : PaginatedRequest
{
    [FromQuery(Name = "search")]
    public string? Search { get; set; }

    public DateTime? Start { get; set; }
    public DateTime? End { get; set; }
}
