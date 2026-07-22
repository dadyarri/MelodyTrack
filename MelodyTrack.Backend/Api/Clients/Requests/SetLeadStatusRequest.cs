using FastEndpoints;

namespace MelodyTrack.Backend.Api.Clients.Requests;

public class SetLeadStatusRequest
{
    [BindFrom("id")]
    public Ulid Id { get; set; }
    public required bool IsClosed { get; set; }
}
