using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Services.Requests;
using MelodyTrack.Backend.Api.Services.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

public class GetServicesEndpoint(AppDbContext db, ServiceToServiceWithCurrentPriceDtoMapConfig mapper, IRecordActivityService recordActivityService)
    : Ep.Req<GetServicesPaginatedRequest>.Res<
        Results<Ok<PaginatedResponse<ServiceWithCurrentPriceDto>>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/services");
    }

    public override async Task<Results<Ok<PaginatedResponse<ServiceWithCurrentPriceDto>>, UnauthorizedHttpResult>>
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
        var latestActivities = await recordActivityService.GetLatestActivitiesAsync(
            "service",
            services.Select(service => service.Id.ToString()).ToArray(),
            ct);

        foreach (var serviceDto in servicesFacets)
        {
            serviceDto.LastActivity = latestActivities.GetValueOrDefault(serviceDto.Id.ToString());
        }

        var totalCount = await db.Services.CountAsync(ct);

        Logger.LogInformation(
            "Retrieved {Count} services (Page {Page} of {TotalPages}, Total: {TotalCount})",
            services.Count,
            req.Page,
            (int)Math.Ceiling(totalCount / (double)req.PageSize),
            totalCount
        );

        return TypedResults.Ok(PaginatedResponse.Create(servicesFacets, totalCount, req));
    }
}
