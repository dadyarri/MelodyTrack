using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class DeleteClientEndpoint(AppDbContext db) : Ep.Req<GetEntityRequest>.Res<Results<NoContent, NotFound>>
{
    public override void Configure()
    {
        Delete("/clients/{id}");
    }

    public override async Task<Results<NoContent, NotFound>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Attempting to delete client with ID: {ClientId}", req.Id);
        var rowsDeleted = await db.Clients.Where(e => e.Id == req.Id).ExecuteDeleteAsync(ct);

        if (rowsDeleted == 0)
        {
            Logger.LogWarning("Failed to delete client: ID {ClientId} not found", req.Id);
            return TypedResults.NotFound();
        }

        Logger.LogInformation("Successfully deleted client with ID: {ClientId}", req.Id);
        return TypedResults.NoContent();
    }
}