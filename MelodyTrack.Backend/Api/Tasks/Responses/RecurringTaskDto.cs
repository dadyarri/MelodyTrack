namespace MelodyTrack.Backend.Api.Tasks.Responses;

public class RecurringTaskDto
{
    public required Ulid RuleId { get; set; }
    public required string Type { get; set; }
    public required string RecipientType { get; set; }
    public required string DeduplicationKey { get; set; }
    public Ulid? ClientId { get; set; }
    public Ulid? TeacherId { get; set; }
    public Ulid? AppointmentId { get; set; }
    public required string Title { get; set; }
    public required string RelatedPersonDisplayName { get; set; }
    public DateTime? RelevantAtUtc { get; set; }
    public required DateOnly BusinessDate { get; set; }
    public string? Phone { get; set; }
    public string? Telegram { get; set; }
    public string? Vk { get; set; }
    public required string PreparedMessage { get; set; }
}
