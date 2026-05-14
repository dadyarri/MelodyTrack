using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public interface IRecordActivityService
{
    Task<Dictionary<string, RecordActivityDto>> GetLatestActivitiesAsync(string entityType, IReadOnlyCollection<string> entityIds, CancellationToken ct);
    Task<RecordActivityDto?> GetLatestActivityAsync(string entityType, string entityId, CancellationToken ct);
}

public class RecordActivityService(AppDbContext db) : IRecordActivityService
{
    public async Task<Dictionary<string, RecordActivityDto>> GetLatestActivitiesAsync(string entityType, IReadOnlyCollection<string> entityIds, CancellationToken ct)
    {
        if (entityIds.Count == 0)
        {
            return [];
        }

        var logs = await db.AuditLogs
            .AsNoTracking()
            .Where(item => item.EntityType == entityType && item.EntityId != null && entityIds.Contains(item.EntityId))
            .OrderByDescending(item => item.CreatedAtUtc)
            .ToListAsync(ct);

        return logs
            .GroupBy(item => item.EntityId!)
            .ToDictionary(
                group => group.Key,
                group => ToDto(group.First()));
    }

    public async Task<RecordActivityDto?> GetLatestActivityAsync(string entityType, string entityId, CancellationToken ct)
    {
        var item = await db.AuditLogs
            .AsNoTracking()
            .Where(log => log.EntityType == entityType && log.EntityId == entityId)
            .OrderByDescending(log => log.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        return item is null ? null : ToDto(item);
    }

    private static RecordActivityDto ToDto(Data.Models.AuditLog item)
    {
        return new RecordActivityDto
        {
            Id = item.Id,
            CreatedAtUtc = item.CreatedAtUtc,
            Category = item.Category,
            Action = item.Action,
            ActorEmail = item.ActorEmail,
            ActorDisplayName = item.ActorDisplayName,
            SourceIpAddress = item.SourceIpAddress,
            Details = item.Details
        };
    }
}
