using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class DeleteClientEndpoint(AppDbContext db, IAuditLogService auditLogService) : Ep.Req<GetEntityRequest>.Res<Results<NoContent, NotFound<ProblemDetails>>>
{
    public override void Configure()
    {
        Delete("/clients/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Attempting to delete client with ID: {ClientId}", req.Id);
        var client = await db.Clients
            .AsNoTracking()
            .Where(e => e.Id == req.Id)
            .Select(e => new { e.Id, e.FirstName, e.LastName })
            .FirstOrDefaultAsync(ct);

        if (client is null)
        {
            Logger.LogWarning("Failed to delete client: ID {ClientId} not found", req.Id);
            AddError(r => r.Id, "Клиент не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
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
