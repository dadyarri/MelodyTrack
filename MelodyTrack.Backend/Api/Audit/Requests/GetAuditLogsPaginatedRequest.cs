using MelodyTrack.Backend.Api.Common.Requests;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Audit.Requests;

public class GetAuditLogsPaginatedRequest : PaginatedRequest
{
    [FromQuery(Name = "search")]
    public string? Search { get; set; }

    [FromQuery(Name = "timezone")]
    public string? Timezone { get; set; }
}
