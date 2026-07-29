using MelodyTrack.Backend.ErrorHandling;

namespace MelodyTrack.Backend.Api.Common.Responses;

public class StaleEntityConflictResponse : ApiProblemDetails
{
    public required string EntityType { get; set; }
    public required string EntityId { get; set; }
    public RecordActivityDto? CurrentActivity { get; set; }
}
