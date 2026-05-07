using FastEndpoints;

namespace MelodyTrack.Backend.Api.Clients.Requests;

public class LookupClientsRequest
{
    [BindFrom("search")]
    public string? Search { get; set; }
}
