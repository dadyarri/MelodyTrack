using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Audit.Requests;
using MelodyTrack.Backend.Api.Audit.Responses;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Audit.Endpoints;

public class GetAuditLogsEndpoint(AppDbContext db) : Ep.Req<GetAuditLogsPaginatedRequest>.Res<Results<Ok<GetAuditLogsResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/audit-logs");
    }

    public override async Task<Results<Ok<GetAuditLogsResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(GetAuditLogsPaginatedRequest req, CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (login is null)
        {
            AddError(_ => login, "Пользователь не авторизован");
            return TypedResults.Unauthorized();
        }

        var user = await db.Users
            .Where(u => u.Email == login.Value)
            .Include(u => u.Role)
            .FirstOrDefaultAsync(ct);

        if (user is null || !user.Role.RoleName.IsAnyAdmin())
        {
            AddError(_ => login, "Нет доступа");
            return TypedResults.Forbid();
        }

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
                Details = item.Details
            })
            .ToListAsync(ct);

        return TypedResults.Ok(new GetAuditLogsResponse
        {
            Data = logs,
            Info = PaginatedResponse.Create(logs, totalCount, req).Info
        });
    }
}
