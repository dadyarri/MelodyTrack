using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using Facet.Extensions.EFCore;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Payments.Requests;
using MelodyTrack.Backend.Api.Payments.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Payments.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/payments")]
public sealed class GetPaymentsEndpoint
{

    public static async Task<Results<Ok<GetPaymentsResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        [AsParameters] GetPaymentsPaginatedRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IRecordActivityService recordActivityService,
        ILogger<GetPaymentsEndpoint> logger,
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
            "Fetching paginated list of payments with filters - Page: {Page}, PageSize: {PageSize}, Client's first name: {FirstName}, Client's last name: {LastName}, Search: {Search}",
            req.Page, req.PageSize,
            req.FirstName ?? "not specified", req.LastName ?? "not specified", req.Search ?? "not specified");

        var paymentsQuery = db.Payments
            .AsNoTracking()
            .Include(e => e.Client)
            .Include(e => e.Service)
            .ApplyFuzzySearchFilters(req)
            .ApplyDateRangeFilter(e => e.Date, req.Start, req.End);

        if (!string.IsNullOrWhiteSpace(req.Search))
        {
            var search = req.Search.Trim().ToLower();
            var pattern = $"%{search}%";

            paymentsQuery = paymentsQuery.Where(e =>
                EF.Functions.ILike(e.Description, pattern)
                || EF.Functions.ILike((e.Client.LastName + " " + e.Client.FirstName + " " + (e.Client.Patronymic ?? "")).Trim(), pattern)
                || (e.Service != null && EF.Functions.ILike(e.Service.Name, pattern)));
        }

        if (req.ClientId.HasValue)
        {
            paymentsQuery = paymentsQuery.Where(e => e.Client.Id == req.ClientId.Value);
        }

        if (req.ServiceId.HasValue)
        {
            paymentsQuery = paymentsQuery.Where(e => e.Service != null && e.Service.Id == req.ServiceId.Value);
        }

        var totalCount = await paymentsQuery.CountAsync(ct);
        var totalAmount = await paymentsQuery.SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        var firstPaymentAtUtc = await paymentsQuery
            .OrderBy(e => e.Date)
            .Select(e => (DateTime?)e.Date)
            .FirstOrDefaultAsync(ct);
        var lastPaymentAtUtc = await paymentsQuery
            .OrderByDescending(e => e.Date)
            .Select(e => (DateTime?)e.Date)
            .FirstOrDefaultAsync(ct);

        var payments = await paymentsQuery
            .OrderByDescending(e => e.Date)
            .ThenBy(e => e.Client.LastName)
            .ApplyPagination(req)
            .ToFacetsAsync<GetPaymentsDto>(ct);

        var paymentActivities = await recordActivityService.GetLatestActivitiesAsync(
            "payment",
            payments.Select(payment => payment.Id.ToString()).ToList(),
            ct);

        foreach (var payment in payments)
        {
            if (paymentActivities.TryGetValue(payment.Id.ToString(), out var activity))
            {
                payment.LastActivity = activity;
            }
        }

        logger.LogInformation(
            "Retrieved {Count} payments (Page {Page} of {TotalPages}, Total: {TotalCount})",
            payments.Count,
            req.EffectivePage,
            (int)Math.Ceiling(totalCount / (double)req.EffectivePageSize),
            totalCount
        );

        var response = PaginatedResponse.Create(payments, totalCount, req);

        return TypedResults.Ok(new GetPaymentsResponse
        {
            Items = response.Items,
            Page = response.Page,
            Summary = new MoneyListSummaryDto
            {
                TotalAmount = totalAmount,
                ItemsCount = (int)totalCount,
                FirstItemAtUtc = firstPaymentAtUtc,
                LastItemAtUtc = lastPaymentAtUtc
            }
        });
    }
}
