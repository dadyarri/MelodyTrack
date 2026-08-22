using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using Facet.Mapping;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Services.Requests;
using MelodyTrack.Backend.Api.Services.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/services")]
public sealed class GetServicesEndpoint
{

    public static async Task<Results<Ok<PaginatedResponse<ServiceWithCurrentPriceDto>>, UnauthorizedHttpResult, ForbidHttpResult>>
        HandleAsync(
        [AsParameters] GetServicesPaginatedRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        ServiceToServiceWithCurrentPriceDtoMapConfig mapper,
        IRecordActivityService recordActivityService,
        ILogger<GetServicesEndpoint> logger,
        CancellationToken ct
    )
    {
        var currentUserRole = (await currentUserAccessor.GetAsync(ct))?.Role.RoleName;
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        logger.LogDebug(
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

        logger.LogInformation(
            "Retrieved {Count} services (Page {Page} of {TotalPages}, Total: {TotalCount})",
            services.Count,
            req.EffectivePage,
            (int)Math.Ceiling(totalCount / (double)req.EffectivePageSize),
            totalCount
        );

        return TypedResults.Ok(PaginatedResponse.Create(servicesFacets, totalCount, req));
    }
}
