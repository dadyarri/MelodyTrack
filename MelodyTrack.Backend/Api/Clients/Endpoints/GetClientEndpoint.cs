using FastEndpoints;
using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientEndpoint(AppDbContext db) : Ep.Req<GetEntityRequest>.Res<IResult>
{
    public override void Configure()
    {
        Get("/clients/{id}");
    }

    public override async Task<IResult> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Fetching client with ID: {ClientId}", req.Id);
        var client = await db.Clients.FirstOrDefaultAsync(e => e.Id == req.Id, ct);

        if (client is null)
        {
            Logger.LogWarning("Client with ID {ClientId} not found", req.Id);
            return ApiResults.NotFound();
        }

        Logger.LogDebug("Successfully retrieved client {FirstName} {LastName} (ID: {ClientId})",
            client.FirstName, client.LastName, client.Id);
        return ApiResults.Ok(client);
    }
}