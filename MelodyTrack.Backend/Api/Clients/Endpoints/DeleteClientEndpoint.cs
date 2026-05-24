using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class DeleteClientEndpoint(AppDbContext db, IAuditLogService auditLogService, IEntityFreshnessService entityFreshnessService) : Ep.Req<GetEntityRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>>
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
            .Select(e => new { e.Id, e.FirstName, e.LastName, e.Patronymic, Phone = e.Contacts.Phone, SourceName = e.Source != null ? e.Source.Name : null })
            .FirstOrDefaultAsync(ct);

        if (client is null)
        {
            Logger.LogInformation("Client with ID {ClientId} was already deleted or not found", req.Id);
            return TypedResults.NoContent();
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "client",
            client.Id,
            req.ExpectedActivityId,
            "Клиент был изменен другим пользователем. Проверьте последние изменения перед удалением.",
            ct);

        if (conflict is not null)
        {
            return TypedResults.Conflict(conflict);
        }

        await db.Clients.Where(e => e.Id == req.Id).ExecuteDeleteAsync(ct);

        Logger.LogInformation("Successfully deleted client with ID: {ClientId}", req.Id);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "clients",
            Action = "client_deleted",
            EntityType = "client",
            EntityId = client.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", $"{client.LastName} {client.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeContext("Отчество", client.Patronymic),
                AuditDetailsFormatter.DescribeContext("Телефон", client.Phone),
                AuditDetailsFormatter.DescribeContext("Источник", client.SourceName)
            )
        }, ct);
        return TypedResults.NoContent();
    }
}
