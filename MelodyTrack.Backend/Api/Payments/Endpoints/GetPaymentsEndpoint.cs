using Facet.Extensions.EFCore;
using FastEndpoints;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Payments.Requests;
using MelodyTrack.Common.Api.Payments.Responses;
using MelodyTrack.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Payments.Endpoints;

public class GetPaymentsEndpoint(AppDbContext db) : Ep.Req<GetPaymentsPaginatedRequest>.Res<IResult>
{
    public override void Configure()
    {
        Get("/payments");
    }

    public override async Task<IResult> ExecuteAsync(GetPaymentsPaginatedRequest req, CancellationToken ct)
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

        return ApiResults.Ok(PaginatedResponse.Create(payments, totalCount, req));

    }
}