using FastEndpoints;

namespace MelodyTrack.Backend.Api.Dashboard.Requests;

public class GetExpensesAnalyticsRequest
{
    [BindFrom("start")]
    public DateTime Start { get; set; }

    [BindFrom("end")]
    public DateTime End { get; set; }

    [BindFrom("timezone")]
    public required string Timezone { get; set; }

    [BindFrom("groupBy")]
    public string? GroupBy { get; set; }
}
