using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Extensions;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientsEndpoint(AppDbContext db, ClientToClientWithBalanceDtoMapConfig mapper)
    : Ep.Req<GetClientsPaginatedRequest>.Res<PaginatedResponse<ClientWithBalanceDto>>
{
    public override void Configure()
    {
        Get("/clients");
    }

    public override async Task<PaginatedResponse<ClientWithBalanceDto>> ExecuteAsync(GetClientsPaginatedRequest req,
        CancellationToken ct)
    {
        var clients = await db.Clients
            .ApplyFilters(req)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ApplyPagination(req)
            .Include(e => e.Contacts)
            .ToListAsync(ct);

        var clientsFacets = await clients.ToFacetsAsync(mapper, ct);

        var totalCount = await db.Clients.CountAsync(ct);

        return PaginatedResponse.Create(clientsFacets, totalCount, req);
    }
}