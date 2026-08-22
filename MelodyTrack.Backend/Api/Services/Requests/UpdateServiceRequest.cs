
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Services.Requests;

public class UpdateServiceRequest
{
    [JsonIgnore]
    public Ulid Id { get; set; }
    public Ulid? ExpectedActivityId { get; set; }
    public required string Name { get; set; }
    public string? PublicName { get; set; }
    public string? Description { get; set; }
    public bool IsConsultation { get; set; }
}
