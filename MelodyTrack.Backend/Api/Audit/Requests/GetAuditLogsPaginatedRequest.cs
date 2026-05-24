using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;

namespace MelodyTrack.Backend.Api.Audit.Requests;

public class GetAuditLogsPaginatedRequest : PaginatedRequest
{
    [BindFrom("search")]
    public string? Search { get; set; }

    [BindFrom("timezone")]
    public string? Timezone { get; set; }
}
