using FastEndpoints;

namespace MelodyTrack.Backend.Api.Dashboard.Requests;

public class GetPaymentsAnalyticsRequest
{
    [BindFrom("start")]
    public DateTime Start { get; set; }

    [BindFrom("end")]
    public DateTime End { get; set; }

    [BindFrom("timezone")]
    public required string Timezone { get; set; }
}
