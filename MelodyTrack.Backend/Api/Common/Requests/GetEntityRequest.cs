namespace MelodyTrack.Backend.Api.Common.Requests;

using FastEndpoints;

public class GetEntityRequest
{
    public Ulid Id { get; set; }

    [BindFrom("expectedActivityId")]
    public Ulid? ExpectedActivityId { get; set; }
}
