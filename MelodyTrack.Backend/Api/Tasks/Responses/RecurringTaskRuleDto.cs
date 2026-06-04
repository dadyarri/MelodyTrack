using MelodyTrack.Backend.Api.Common.Responses;

namespace MelodyTrack.Backend.Api.Tasks.Responses;

public class RecurringTaskRuleDto
{
    public required Ulid Id { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; }
    public required bool IsEnabled { get; set; }
    public required string MessageTemplate { get; set; }
    public int? OffsetMinutes { get; set; }
    public int? CooldownDays { get; set; }
    public RecordActivityDto? LastActivity { get; set; }
}
