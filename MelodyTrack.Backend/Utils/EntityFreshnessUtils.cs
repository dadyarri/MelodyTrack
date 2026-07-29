using MelodyTrack.Backend.Api.Common.Responses;

namespace MelodyTrack.Backend.Utils;

public static class EntityFreshnessUtils
{
    public static bool IsStale(Ulid? expectedActivityId, RecordActivityDto? currentActivity)
    {
        return expectedActivityId.HasValue && expectedActivityId != currentActivity?.Id;
    }

}
