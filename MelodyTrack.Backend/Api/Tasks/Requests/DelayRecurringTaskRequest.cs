using FastEndpoints;

namespace MelodyTrack.Backend.Api.Tasks.Requests;

public class DelayRecurringTaskRequest
{
    public required string Timezone { get; set; }
    public required Ulid RuleId { get; set; }
    public required string Type { get; set; }
    [BindFrom("taskId")]
    public string DeduplicationKey { get; set; } = string.Empty;
    public required DateTime DelayUntilUtc { get; set; }
    public Ulid? ClientId { get; set; }
    public Ulid? TeacherId { get; set; }
    public Ulid? AppointmentId { get; set; }
}
