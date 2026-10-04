using MelodyTrack.Backend.Api.Common.Requests;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Clients.Requests;

public class GetClientHistoryRequest : PaginatedRequest
{
    [FromRoute]
    public Ulid Id { get; set; }

    [FromQuery(Name = "expectedActivityId")]
    public Ulid? ExpectedActivityId { get; set; }
}
