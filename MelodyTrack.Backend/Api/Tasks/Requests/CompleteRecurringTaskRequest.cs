
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;

namespace MelodyTrack.Backend.Api.Tasks.Requests;

public class CompleteRecurringTaskRequest
{
    public required string Timezone { get; set; }
    public required Ulid RuleId { get; set; }
    public required string Type { get; set; }
    [JsonIgnore]
    public string DeduplicationKey { get; set; } = string.Empty;
    public Ulid? ClientId { get; set; }
    public Ulid? TeacherId { get; set; }
    public Ulid? AppointmentId { get; set; }
    public string? PreparedMessage { get; set; }
}
