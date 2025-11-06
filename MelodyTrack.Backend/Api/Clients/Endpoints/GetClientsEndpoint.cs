using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientsEndpoint(AppDbContext db, ClientToClientWithBalanceDtoMapConfig mapper)
    : Ep.Req<PaginatedRequest>.Res<PaginatedResponse<ClientWithBalanceDto>>
{
    public override void Configure()
    {
        Get("/clients");
    }

    public override async Task<PaginatedResponse<ClientWithBalanceDto>> ExecuteAsync(PaginatedRequest req,
        CancellationToken ct)
    {
        var skipped = req.PageSize * (req.Page - 1);
        var clients = await db.Clients
            .Skip(skipped)
            .Take(req.PageSize)
            .Include(e => e.Contacts)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ToFacetsAsync(mapper, ct);

        var totalCount = await db.Clients.CountAsync(ct);

        return PaginatedResponse.Create(clients, totalCount, req);
    }
}