using Facet.Extensions;
using FastEndpoints;
using MelodyTrack.Common.Api.Clients.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class LookupClientsEndpoint(AppDbContext db) : Ep.NoReq.Res<IResult>
{
    public override void Configure()
    {
        Get("/clients/lookup");
    }

    public override async Task<IResult> ExecuteAsync(CancellationToken ct)
    {
        Logger.LogDebug("Fetching lookup list of all clients");
        var clients = await db.Clients
            .SelectFacet<LookupClientDto>()
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(ct);

        Logger.LogInformation("Retrieved {Count} clients for lookup list", clients.Count);

        return ApiResults.Ok(new LookupClientsResponse
        {
            Clients = clients
        });
    }
}