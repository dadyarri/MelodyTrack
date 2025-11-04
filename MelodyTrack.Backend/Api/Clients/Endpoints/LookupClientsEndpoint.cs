using Facet.Extensions;
using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Data;
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
        var clients = await db.Clients
            .SelectFacet<LookupClientDto>()
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToListAsync(ct);
        
        return new LookupClientsResponse
        {
            Clients = clients
        };
    }
}