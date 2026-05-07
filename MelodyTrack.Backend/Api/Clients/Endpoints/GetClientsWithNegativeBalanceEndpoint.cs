using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientsWithNegativeBalanceEndpoint(AppDbContext db, ClientToClientWithBalanceDtoMapConfig mapper)
    : Ep.NoReq.Res<Results<Ok<GetClientsWithNegativeBalanceResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/clients/inDebt");
    }

    public override async Task<Results<Ok<GetClientsWithNegativeBalanceResponse>, UnauthorizedHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var clients = await db.Clients
            .AsNoTracking()
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Include(e => e.Contacts)
            .ToListAsync(ct);

        var clientsFacets = await clients.ToFacetsAsync(mapper, ct);
        var debtors = clientsFacets.Where(e => e.Balance < 0).ToList();

        return TypedResults.Ok(new GetClientsWithNegativeBalanceResponse
        {
            Debtors = debtors
        });
    }
}