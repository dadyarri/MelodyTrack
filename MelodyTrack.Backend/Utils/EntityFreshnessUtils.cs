using MelodyTrack.Backend.Api.Common.Responses;

namespace MelodyTrack.Backend.Utils;

public static class EntityFreshnessUtils
{
    public static bool IsStale(Ulid? expectedActivityId, RecordActivityDto? currentActivity)
    {
        return expectedActivityId.HasValue && expectedActivityId != currentActivity?.Id;
    }

    public static StaleEntityConflictResponse CreateConflict(string entityType, Ulid entityId, string message, RecordActivityDto? currentActivity)
    {
        return new StaleEntityConflictResponse
        {
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Message = message,
            CurrentActivity = currentActivity
        };
    }
}
