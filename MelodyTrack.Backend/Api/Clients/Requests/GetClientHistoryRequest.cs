using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;

namespace MelodyTrack.Backend.Api.Clients.Requests;

public class GetClientHistoryRequest : PaginatedRequest
{
    public Ulid Id { get; set; }

    [BindFrom("expectedActivityId")]
    public Ulid? ExpectedActivityId { get; set; }
}
