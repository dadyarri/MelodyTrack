using FastEndpoints;
using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class DeleteClientEndpoint(AppDbContext db) : Ep.Req<GetEntityRequest>.Res<Results<NoContent, NotFound<ProblemDetails>>>
{
    public override void Configure()
    {
        Delete("/clients/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Attempting to delete client with ID: {ClientId}", req.Id);
        var rowsDeleted = await db.Clients.Where(e => e.Id == req.Id).ExecuteDeleteAsync(ct);

        if (rowsDeleted == 0)
        {
            Logger.LogWarning("Failed to delete client: ID {ClientId} not found", req.Id);
            AddError(r => r.Id, "Клиент не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        Logger.LogInformation("Successfully deleted client with ID: {ClientId}", req.Id);
        return TypedResults.NoContent();
    }
}