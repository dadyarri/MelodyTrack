using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Utils;

namespace MelodyTrack.Backend.Services;

public interface IEntityFreshnessService
{
    Task<RecordActivityDto?> GetLatestActivityAsync(string entityType, Ulid entityId, CancellationToken ct);
    Task<StaleEntityConflictResponse?> GetConflictIfStaleAsync(string entityType, Ulid entityId, Ulid? expectedActivityId, string message, CancellationToken ct);
}

public class EntityFreshnessService(IRecordActivityService recordActivityService) : IEntityFreshnessService
{
    public Task<RecordActivityDto?> GetLatestActivityAsync(string entityType, Ulid entityId, CancellationToken ct)
    {
        return recordActivityService.GetLatestActivityAsync(entityType, entityId.ToString(), ct);
    }

    public async Task<StaleEntityConflictResponse?> GetConflictIfStaleAsync(string entityType, Ulid entityId, Ulid? expectedActivityId, string message, CancellationToken ct)
    {
        var latestActivity = await GetLatestActivityAsync(entityType, entityId, ct);
        if (!EntityFreshnessUtils.IsStale(expectedActivityId, latestActivity))
        {
            return null;
        }

        return EntityFreshnessUtils.CreateConflict(entityType, entityId, message, latestActivity);
    }
}
