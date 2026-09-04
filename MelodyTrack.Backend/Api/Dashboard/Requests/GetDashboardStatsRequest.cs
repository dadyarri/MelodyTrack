
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Dashboard.Requests;

public class GetDashboardStatsRequest
{
    [FromQuery(Name = "timezone")]
    public required string Timezone { get; set; }
}
