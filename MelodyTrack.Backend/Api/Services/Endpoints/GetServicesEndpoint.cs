using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Services.Requests;
using MelodyTrack.Common.Api.Services.Responses;
using MelodyTrack.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

public class GetServicesEndpoint(AppDbContext db, ServiceToServiceWithCurrentPriceDtoMapConfig mapper)
    : Ep.Req<GetServicesPaginatedRequest>.Res<
        IResult>
{
    public override void Configure()
    {
        Get("/services");
    }

    public override async Task<IResult>
        ExecuteAsync(GetServicesPaginatedRequest req, CancellationToken ct)
    {
        Logger.LogDebug(
            "Fetching paginated list of services with filters - Page: {Page}, PageSize: {PageSize}, Name: {Name}",
            req.Page, req.PageSize,
            req.Name ?? "not specified");
        var services = await db.Services
            .AsNoTracking()
            .ApplyFuzzySearchFilters(req)
            .OrderBy(e => e.Name)
            .ApplyPagination(req)
            .ToListAsync(ct);

        var servicesFacets = await services.ToFacetsAsync(mapper, ct);
        var totalCount = await db.Services.CountAsync(ct);

        Logger.LogInformation(
            "Retrieved {Count} services (Page {Page} of {TotalPages}, Total: {TotalCount})",
            services.Count,
            req.Page,
            (int)Math.Ceiling(totalCount / (double)req.PageSize),
            totalCount
        );

        return ApiResults.Ok(PaginatedResponse.Create(servicesFacets, totalCount, req));
    }
}