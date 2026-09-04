using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Common.Requests;

namespace MelodyTrack.Backend.Api.Clients.Requests;

public class GetClientHistoryRequest : PaginatedRequest
{
        [FromRoute]
    public Ulid Id { get; set; }

    [FromQuery(Name = "expectedActivityId")]
    public Ulid? ExpectedActivityId { get; set; }
}
