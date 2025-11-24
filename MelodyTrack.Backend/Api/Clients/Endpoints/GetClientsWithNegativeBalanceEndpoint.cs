using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Common.Api.Clients.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientsWithNegativeBalanceEndpoint(AppDbContext db, ClientToClientWithBalanceDtoMapConfig mapper)
    : Ep.NoReq.Res<IResult>
{
    public override void Configure()
    {
        Get("/clients/inDebt");
    }

    public override async Task<IResult> ExecuteAsync(CancellationToken ct)
    {
        var clients = await db.Clients
            .AsNoTracking()
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Include(e => e.Contacts)
            .ToListAsync(ct);

        var clientsFacets = await clients.ToFacetsAsync(mapper, ct);
        var debtors = clientsFacets.Where(e => e.Balance < 0).ToList();

        return ApiResults.Ok(new GetClientsWithNegativeBalanceResponse
        {
            Debtors = debtors
        });
    }
}