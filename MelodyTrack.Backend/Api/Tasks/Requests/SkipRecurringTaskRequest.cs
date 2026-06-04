namespace MelodyTrack.Backend.Api.Tasks.Requests;

public class SkipRecurringTaskRequest
{
    public required string Timezone { get; set; }
    public required Ulid RuleId { get; set; }
    public required string Type { get; set; }
    public required string DeduplicationKey { get; set; }
    public Ulid? ClientId { get; set; }
    public Ulid? AppointmentId { get; set; }
}
