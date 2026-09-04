using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Common.Requests;


public class GetEntityRequest
{
        [FromRoute]
    public Ulid Id { get; set; }

    [FromQuery(Name = "expectedActivityId")]
    public Ulid? ExpectedActivityId { get; set; }
}
