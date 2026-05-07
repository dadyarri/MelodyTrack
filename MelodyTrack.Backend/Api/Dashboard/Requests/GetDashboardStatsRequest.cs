using FastEndpoints;

namespace MelodyTrack.Backend.Api.Dashboard.Requests;

public class GetDashboardStatsRequest
{
    [BindFrom("timezone")]
    public required string Timezone { get; set; }
}
