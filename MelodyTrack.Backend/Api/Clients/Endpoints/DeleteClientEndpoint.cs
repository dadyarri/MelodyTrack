using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class DeleteClientEndpoint(AppDbContext db, IAuditLogService auditLogService) : Ep.Req<GetEntityRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Delete("/clients/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Attempting to delete client with ID: {ClientId}", req.Id);
        var client = await db.Clients
            .AsNoTracking()
            .Where(e => e.Id == req.Id)
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .FirstOrDefaultAsync(ct);

        if (client is null)
        {
            Logger.LogInformation("Client with ID {ClientId} was already deleted or not found", req.Id);
            return TypedResults.NoContent();
        }

        var latestActivity = await db.AuditLogs
            .AsNoTracking()
            .Where(item => item.EntityType == "client" && item.EntityId == client.Id.ToString())
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => new RecordActivityDto
            {
                Id = item.Id,
                CreatedAtUtc = item.CreatedAtUtc,
                Category = item.Category,
                Action = item.Action,
                ActorEmail = item.ActorEmail,
                ActorDisplayName = item.ActorDisplayName,
                SourceIpAddress = item.SourceIpAddress,
                Details = item.Details
            })
            .FirstOrDefaultAsync(ct);

        if (EntityFreshnessUtils.IsStale(req.ExpectedActivityId, latestActivity))
        {
            return TypedResults.Conflict(EntityFreshnessUtils.CreateConflict(
                "client",
                client.Id,
                "Клиент был изменен другим пользователем. Проверьте последние изменения перед удалением.",
                latestActivity));
        }

        await db.Clients.Where(e => e.Id == req.Id).ExecuteDeleteAsync(ct);

        Logger.LogInformation("Successfully deleted client with ID: {ClientId}", req.Id);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "clients",
            Action = "client_deleted",
            EntityType = "client",
            EntityId = client.Id.ToString(),
            Details = $"{client.LastName} {client.FirstName}".Trim()
        }, ct);
        return TypedResults.NoContent();
    }
}
