namespace MelodyTrack.Backend.Api.Common.Responses;

public class RecordActivityDto
{
    public required DateTime CreatedAtUtc { get; set; }
    public required string Category { get; set; }
    public required string Action { get; set; }
    public string? ActorEmail { get; set; }
    public string? ActorDisplayName { get; set; }
    public string? SourceIpAddress { get; set; }
    public string? Details { get; set; }
}
