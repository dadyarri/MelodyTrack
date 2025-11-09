using Facet.Extensions.EFCore;
using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Payments.Requests;
using MelodyTrack.Backend.Api.Payments.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Extensions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Payments.Endpoints;

public class GetPaymentsEndpoint(AppDbContext db) : Ep.Req<GetPaymentsPaginatedRequest>.Res<Results<Ok<PaginatedResponse<GetPaymentsDto>>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/payments");
    }

    public override async Task<Results<Ok<PaginatedResponse<GetPaymentsDto>>, UnauthorizedHttpResult>> ExecuteAsync(GetPaymentsPaginatedRequest req, CancellationToken ct)
    {
        Logger.LogDebug(
            "Fetching paginated list of payments with filters - Page: {Page}, PageSize: {PageSize}, Client's first name: {FirstName}, Client's last name: {LastName}",
            req.Page, req.PageSize,
            req.FirstName ?? "not specified", req.LastName ?? "not specified");

        var payments = await db.Payments
            .AsNoTracking()
            .Include(e => e.Client)
            .OrderBy(e => e.Client.LastName)
            .ThenBy(e => e.Client.FirstName)
            .ApplyFuzzySearchFilters(req)
            .ApplyPagination(req)
            .Include(e => e.Service)
            .ToFacetsAsync<GetPaymentsDto>(ct);

        var totalCount = await db.Clients.CountAsync(ct);

        Logger.LogInformation(
            "Retrieved {Count} payments (Page {Page} of {TotalPages}, Total: {TotalCount})",
            payments.Count,
            req.Page,
            (int)Math.Ceiling(totalCount / (double)req.PageSize),
            totalCount
        );

        return TypedResults.Ok(PaginatedResponse.Create(payments, totalCount, req));

    }
}