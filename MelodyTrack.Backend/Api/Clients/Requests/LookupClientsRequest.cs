
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Clients.Requests;

public class LookupClientsRequest
{
    [FromQuery(Name = "search")]
    public string? Search { get; set; }
}
