using FastEndpoints;

namespace MelodyTrack.Backend.Api.Services.Requests;

public class UpdateServiceRequest
{
    [BindFrom("id")]
    public Ulid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
}
