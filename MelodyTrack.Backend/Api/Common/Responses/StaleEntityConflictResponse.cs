namespace MelodyTrack.Backend.Api.Common.Responses;

public class StaleEntityConflictResponse
{
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public required string Message { get; set; }
    public RecordActivityDto? CurrentActivity { get; set; }
}
