namespace MelodyTrack.Backend.Api.Schedule.Responses;

public class LookupRecurrenceTypeDto
{
    public required Ulid Id { get; set; }
    public required string Key { get; set; }
    public required string DisplayName { get; set; }
}
