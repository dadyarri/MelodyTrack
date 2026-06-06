using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ClientSources.Endpoints;

public class DeleteClientSourceEndpoint(AppDbContext db, IAuditLogService auditLogService, IEntityFreshnessService entityFreshnessService)
    : Ep.Req<GetEntityRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Delete("/client-sources/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult, ForbidHttpResult, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(
        GetEntityRequest req,
        CancellationToken ct)
    {
        var currentUserRole = await EndpointAuthUtils.GetCurrentUserRoleAsync(User, db, ct);
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var source = await db.ClientSources
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == req.Id, ct);

        if (source is null)
        {
            return TypedResults.NoContent();
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "client_source",
            source.Id,
            req.ExpectedActivityId,
            "Источник клиента был изменен другим пользователем. Проверьте последние изменения перед удалением.",
            ct);

        if (conflict is not null)
        {
            return TypedResults.Conflict(conflict);
        }

        await db.Clients
            .Where(e => e.SourceId == req.Id)
            .ExecuteUpdateAsync(updates => updates.SetProperty(client => client.SourceId, _ => null), ct);

        await db.ClientSources
            .Where(e => e.Id == req.Id)
            .ExecuteDeleteAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "clients",
            Action = "client_source_deleted",
            EntityType = "client_source",
            EntityId = source.Id.ToString(),
            Details = AuditDetailsFormatter.DescribeContext("Источник клиента", source.Name)
        }, ct);

        return TypedResults.NoContent();
    }
}
