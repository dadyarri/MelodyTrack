using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Common.Api.Clients.Requests;
using MelodyTrack.Common.Api.Clients.Responses;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientsEndpoint(AppDbContext db, ClientToClientWithBalanceDtoMapConfig mapper)
    : Ep.Req<GetClientsPaginatedRequest>.Res<
        IResult>
{
    public override void Configure()
    {
        Get("/clients");
    }

    public override async Task<IResult>
        ExecuteAsync(GetClientsPaginatedRequest req,
            CancellationToken ct)
    {
        Logger.LogDebug(
            "Fetching paginated list of clients with filters - Page: {Page}, PageSize: {PageSize}, FirstName: {FirstName},  LastName: {LastName}",
            req.Page, req.PageSize,
            req.FirstName ?? "not specified", req.LastName ?? "not specified");
        var clients = await db.Clients
            .AsNoTracking()
            .ApplyFuzzySearchFilters(req)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ApplyPagination(req)
            .Include(e => e.Contacts)
            .ToListAsync(ct);

        var clientsFacets = await clients.ToFacetsAsync(mapper, ct);

        var totalCount = await db.Clients.CountAsync(ct);

        Logger.LogInformation(
            "Retrieved {Count} clients (Page {Page} of {TotalPages}, Total: {TotalCount})",
            clients.Count,
            req.Page,
            (int)Math.Ceiling(totalCount / (double)req.PageSize),
            totalCount
        );

        return ApiResults.Ok(PaginatedResponse.Create(clientsFacets, totalCount, req));
    }
}