namespace MelodyTrack.Backend.Api.Common.Responses;

public class ReferenceBookItemDto
{
    public required Ulid Id { get; set; }
    public required string Name { get; set; }
    public RecordActivityDto? LastActivity { get; set; }
}
