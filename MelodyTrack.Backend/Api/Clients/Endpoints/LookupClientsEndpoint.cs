using Facet.Extensions;
using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Extensions;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class LookupClientsEndpoint(AppDbContext db) : Ep.Req<LookupClientsRequest>.Res<LookupClientsResponse>
{
    public override void Configure()
    {
        Get("/clients/lookup");
    }

    public override async Task<LookupClientsResponse> ExecuteAsync(LookupClientsRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Fetching lookup list of clients with search: {Search}", req.Search ?? "not specified");
        var clients = await db.Clients
            .ApplyClientFullNameSearch(req.Search)
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
