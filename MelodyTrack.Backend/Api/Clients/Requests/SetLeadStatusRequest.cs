
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Clients.Requests;

public class SetLeadStatusRequest
{
    [JsonIgnore]
    public Ulid Id { get; set; }
    public required bool IsClosed { get; set; }
}
