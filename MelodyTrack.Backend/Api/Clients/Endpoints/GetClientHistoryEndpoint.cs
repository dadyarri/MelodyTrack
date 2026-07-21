using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientHistoryEndpoint(AppDbContext db, ClientToClientWithBalanceDtoMapConfig mapper, IRecordActivityService recordActivityService)
    : Ep.Req<GetClientHistoryRequest>.Res<Results<Ok<ClientHistoryResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>>>
{
    public override void Configure()
    {
        Get("/clients/{id}/history");
    }

    public override async Task<Results<Ok<ClientHistoryResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>>> ExecuteAsync(
        GetClientHistoryRequest req,
        CancellationToken ct)
    {
        var currentUserRole = await EndpointAuthUtils.GetCurrentUserRoleAsync(User, db, ct);
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        Logger.LogDebug("Fetching history for client {ClientId}", req.Id);

        var client = await db.Clients
            .AsNoTracking()
            .Include(e => e.Contacts)
            .Include(e => e.Source)
            .FirstOrDefaultAsync(e => e.Id == req.Id, ct);

        if (client is null)
        {
            AddError(r => r.Id, "Клиент не найден");
            return TypedResults.NotFound(ApiErrorResponseFactory.CreateValidationProblemDetails(
                ValidationFailures,
                HttpContext,
                StatusCodes.Status404NotFound));
        }

        var clientDto = (await new[] { client }.ToList().ToFacetsAsync(mapper, ct)).Single();
        clientDto.LastActivity = await recordActivityService.GetLatestActivityAsync("client", client.Id.ToString(), ct);

        var sourceEventCount = req.Page * req.PageSize;
        var paymentEvents = await db.Payments
            .AsNoTracking()
            .Where(e => e.Client.Id == client.Id)
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .Select(e => new ClientFinancialHistoryEventDto
            {
                Id = e.Id,
                Type = "top_up",
                Amount = e.Amount,
                Date = e.Date,
                Description = e.Description,
                ServiceName = e.Service != null ? e.Service.Name : null
            })
            .Take(sourceEventCount)
            .ToListAsync(ct);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Where(e => e.Client.Id == client.Id
                        && (e.Status == AppointmentStatus.Completed || e.Status == AppointmentStatus.Burned)
                        && !e.IsDeleted)
            .Select(e => new
            {
                Id = e.Id,
                Date = e.StartDate,
                ServiceId = e.Service.Id,
                ServiceName = e.Service.Name,
                ProviderDisplayName = e.Provider == null
                    ? null
                    : e.Provider.FirstName + " " + e.Provider.LastName,
                AppointmentStatus = e.Status.ToApiKey()
            })
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .Take(sourceEventCount)
            .ToListAsync(ct);

        var serviceIds = appointments.Select(e => e.ServiceId).Distinct().ToList();
        var priceLookup = await db.ServicePriceHistory
            .AsNoTracking()
            .Where(e => serviceIds.Contains(e.Service.Id))
            .Select(e => new { ServiceId = e.Service.Id, e.EffectiveDate, e.Price })
            .ToListAsync(ct);

        var pricesByService = priceLookup
            .GroupBy(e => e.ServiceId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(e => new ServicePriceSnapshot(e.EffectiveDate, e.Price)).ToList());

        var appointmentEvents = appointments.Select(e => new ClientFinancialHistoryEventDto
        {
            Id = e.Id,
            Type = "appointment",
            Amount = -ClientBalanceCalculator.ResolveServiceCost(e.ServiceId, e.Date, pricesByService),
            Date = e.Date,
            ServiceName = e.ServiceName,
            ProviderDisplayName = e.ProviderDisplayName,
            AppointmentStatus = e.AppointmentStatus
        }).ToList();

        var eventPage = paymentEvents
            .Concat(appointmentEvents)
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .Skip(req.PageSize * (req.Page - 1))
            .Take(req.PageSize)
            .ToList();

        var totalPayments = await db.Payments
            .AsNoTracking()
            .Where(e => e.Client.Id == client.Id)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        var paymentsCount = await db.Payments
            .AsNoTracking()
            .CountAsync(e => e.Client.Id == client.Id, ct);

        var completedAppointmentsQuery = db.Appointments
            .AsNoTracking()
            .Where(e => e.Client.Id == client.Id
                        && (e.Status == AppointmentStatus.Completed || e.Status == AppointmentStatus.Burned)
                        && !e.IsDeleted);

        var completedAppointmentsCount = await completedAppointmentsQuery.CountAsync(ct);
        var lastVisitAtUtc = await completedAppointmentsQuery
            .OrderByDescending(e => e.StartDate)
            .Select(e => (DateTime?)e.StartDate)
            .FirstOrDefaultAsync(ct);

        var upcomingAppointmentsQuery = db.Appointments
            .AsNoTracking()
            .Where(e => e.Client.Id == client.Id
                        && e.Status == AppointmentStatus.Planned
                        && !e.IsDeleted
                        && e.StartDate >= DateTime.UtcNow);

        var upcomingAppointmentsCount = await upcomingAppointmentsQuery.CountAsync(ct);
        var nextAppointmentAtUtc = await upcomingAppointmentsQuery
            .OrderBy(e => e.StartDate)
            .Select(e => (DateTime?)e.StartDate)
            .FirstOrDefaultAsync(ct);

        var lastPaymentAtUtc = await db.Payments
            .AsNoTracking()
            .Where(e => e.Client.Id == client.Id)
            .OrderByDescending(e => e.Date)
            .Select(e => (DateTime?)e.Date)
            .FirstOrDefaultAsync(ct);

        return TypedResults.Ok(new ClientHistoryResponse
        {
            Client = clientDto,
            Summary = new ClientHistorySummaryDto
            {
                TotalPayments = totalPayments,
                PaymentsCount = paymentsCount,
                CompletedAppointmentsCount = completedAppointmentsCount,
                UpcomingAppointmentsCount = upcomingAppointmentsCount,
                LastPaymentAtUtc = lastPaymentAtUtc,
                LastVisitAtUtc = lastVisitAtUtc,
                NextAppointmentAtUtc = nextAppointmentAtUtc
            },
            Events = PaginatedResponse.Create(eventPage, paymentsCount + completedAppointmentsCount, req)
        });
    }
}
