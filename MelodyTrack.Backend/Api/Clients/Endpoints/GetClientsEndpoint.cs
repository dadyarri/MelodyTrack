using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientsEndpoint(AppDbContext db, ClientToClientWithBalanceDtoMapConfig mapper, IRecordActivityService recordActivityService)
    : Ep.Req<GetClientsPaginatedRequest>.Res<
        Results<Ok<PaginatedResponse<ClientWithBalanceDto>>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/clients");
    }

    public override async Task<Results<Ok<PaginatedResponse<ClientWithBalanceDto>>, UnauthorizedHttpResult>>
        ExecuteAsync(GetClientsPaginatedRequest req,
            CancellationToken ct)
    {
        Logger.LogDebug(
            "Fetching paginated list of clients with filters - Page: {Page}, PageSize: {PageSize}, FirstName: {FirstName}, LastName: {LastName}, Search: {Search}",
            req.Page, req.PageSize,
            req.FirstName ?? "not specified", req.LastName ?? "not specified", req.Search ?? "not specified");

        var clientsQuery = db.Clients
            .AsNoTracking()
            .ApplyFuzzySearchFilters(req)
            .ApplyClientFullNameSearch(req.Search);

        var totalCount = await clientsQuery.CountAsync(ct);

        var clients = await clientsQuery
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .ApplyPagination(req)
            .Include(e => e.Contacts)
            .Include(e => e.Source)
            .ToListAsync(ct);

        var clientsFacets = await clients.ToFacetsAsync(mapper, ct);
        var clientActivities = await recordActivityService.GetLatestActivitiesAsync(
            "client",
            clientsFacets.Select(client => client.Id.ToString()).ToList(),
            ct);

        foreach (var client in clientsFacets)
        {
            if (clientActivities.TryGetValue(client.Id.ToString(), out var activity))
            {
                client.LastActivity = activity;
            }
        }

        Logger.LogInformation(
            "Retrieved {Count} clients (Page {Page} of {TotalPages}, Total: {TotalCount})",
            clients.Count,
            req.Page,
            (int)Math.Ceiling(totalCount / (double)req.PageSize),
            totalCount
        );

        return TypedResults.Ok(PaginatedResponse.Create(clientsFacets, totalCount, req));
    }
}
