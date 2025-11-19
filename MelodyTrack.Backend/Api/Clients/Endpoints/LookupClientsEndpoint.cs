using Facet.Extensions;
using FastEndpoints;
using MelodyTrack.Common.Api.Clients.Responses;
using MelodyTrack.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class LookupClientsEndpoint(AppDbContext db) : Ep.NoReq.Res<LookupClientsResponse>
{
    public override void Configure()
    {
        Get("/clients/lookup");
    }

    public override async Task<LookupClientsResponse> ExecuteAsync(CancellationToken ct)
    {
        Logger.LogDebug("Fetching lookup list of all clients");
        var clients = await db.Clients
            .SelectFacet<LookupClientDto>()
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(ct);

        Logger.LogInformation("Retrieved {Count} clients for lookup list", clients.Count);

        return new LookupClientsResponse
        {
            Clients = clients
        };
    }
}