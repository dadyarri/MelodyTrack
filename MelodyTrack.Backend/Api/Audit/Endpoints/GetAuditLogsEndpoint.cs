using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Audit.Requests;
using MelodyTrack.Backend.Api.Audit.Responses;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Audit.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/audit-logs")]
public sealed class GetAuditLogsEndpoint
{
    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Superuser)]
    public static async Task<Results<Ok<GetAuditLogsResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        [AsParameters] GetAuditLogsPaginatedRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        CancellationToken ct
    )
    {
        var timezone = ResolveTimezoneOrUtc(req.Timezone);
        var normalizedSearch = req.Search?.Trim().ToLowerInvariant();

        var query = db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAtUtc)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var pattern = $"%{normalizedSearch}%";
            query = query.Where(item =>
                EF.Functions.ILike(item.Category, pattern) ||
                EF.Functions.ILike(item.Action, pattern) ||
                EF.Functions.ILike(item.EntityType, pattern) ||
                (item.EntityId != null && EF.Functions.ILike(item.EntityId, pattern)) ||
                (item.ActorEmail != null && EF.Functions.ILike(item.ActorEmail, pattern)) ||
                (item.ActorDisplayName != null && EF.Functions.ILike(item.ActorDisplayName, pattern)) ||
                (item.SourceIpAddress != null && EF.Functions.ILike(item.SourceIpAddress, pattern)) ||
                (item.Details != null && EF.Functions.ILike(item.Details, pattern)));
        }

        var totalCount = await query.LongCountAsync(ct);
        var logs = await query
            .ApplyPagination(req)
            .Select(item => new GetAuditLogsDto
            {
                Id = item.Id,
                CreatedAtUtc = item.CreatedAtUtc,
                Category = item.Category,
                Action = item.Action,
                EntityType = item.EntityType,
                EntityId = item.EntityId,
                ActorEmail = item.ActorEmail,
                ActorDisplayName = item.ActorDisplayName,
                SourceIpAddress = item.SourceIpAddress,
                Details = AuditDetailsFormatter.FormatForDisplay(item.Details, timezone)
            })
            .ToListAsync(ct);

        return TypedResults.Ok(new GetAuditLogsResponse
        {
            Items = logs,
            Page = PaginatedResponse.Create(logs, totalCount, req).Page
        });
    }

    private static TimeZoneInfo ResolveTimezoneOrUtc(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return TimeZoneInfo.Utc;
        }

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }
}
