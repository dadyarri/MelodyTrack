using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Services.RecurringTasks;

internal sealed class RecurringTaskCandidate
{
    public required Ulid RuleId { get; init; }
    public required RecurringTaskType Type { get; init; }
    public required RecurringTaskRecipientType RecipientType { get; init; }
    public required string DeduplicationKey { get; init; }
    public Ulid? ClientId { get; init; }
    public Ulid? TeacherId { get; init; }
    public Ulid? AppointmentId { get; init; }
    public required string Title { get; init; }
    public required string RelatedPersonDisplayName { get; init; }
    public DateTime? RelevantAtUtc { get; init; }
    public required DateOnly BusinessDate { get; init; }
    public string? Phone { get; init; }
    public string? Telegram { get; init; }
    public string? Vk { get; init; }
    public required string PreparedMessage { get; init; }
    public required DateTime SortAtUtc { get; init; }
}
