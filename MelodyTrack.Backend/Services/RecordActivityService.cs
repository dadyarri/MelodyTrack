using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public interface IRecordActivityService
{
    Task<List<RecordActivityDto>> GetRecentClientActivityAsync(Ulid clientId, string firstName, string lastName, string? patronymic, CancellationToken ct);
    Task<Dictionary<string, RecordActivityDto>> GetLatestActivitiesAsync(string entityType, IReadOnlyCollection<string> entityIds, CancellationToken ct);
}

public class RecordActivityService(AppDbContext db) : IRecordActivityService
{
    public async Task<List<RecordActivityDto>> GetRecentClientActivityAsync(Ulid clientId, string firstName, string lastName, string? patronymic, CancellationToken ct)
    {
        var fullName = $"{lastName} {firstName}".Trim();
        var patronymicName = string.IsNullOrWhiteSpace(patronymic)
            ? null
            : $"{lastName} {firstName} {patronymic}".Trim();

        var query = db.AuditLogs
            .AsNoTracking()
            .Where(item => item.EntityType == "client" && item.EntityId == clientId.ToString());

        if (!string.IsNullOrWhiteSpace(fullName))
        {
            var fullNamePattern = $"%{fullName}%";
            query = query.Concat(db.AuditLogs.AsNoTracking().Where(item =>
                (item.EntityType == "payment" || item.EntityType == "appointment") &&
                item.Details != null &&
                EF.Functions.ILike(item.Details, fullNamePattern)));
        }

        if (!string.IsNullOrWhiteSpace(patronymicName))
        {
            var patronymicPattern = $"%{patronymicName}%";
            query = query.Concat(db.AuditLogs.AsNoTracking().Where(item =>
                (item.EntityType == "payment" || item.EntityType == "appointment") &&
                item.Details != null &&
                EF.Functions.ILike(item.Details, patronymicPattern)));
        }

        return await query
            .OrderByDescending(item => item.CreatedAtUtc)
            .Take(8)
            .Select(item => new RecordActivityDto
            {
                CreatedAtUtc = item.CreatedAtUtc,
                Category = item.Category,
                Action = item.Action,
                ActorEmail = item.ActorEmail,
                ActorDisplayName = item.ActorDisplayName,
                SourceIpAddress = item.SourceIpAddress,
                Details = item.Details
            })
            .ToListAsync(ct);
    }

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

    private static RecordActivityDto ToDto(Data.Models.AuditLog item)
    {
        return new RecordActivityDto
        {
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
