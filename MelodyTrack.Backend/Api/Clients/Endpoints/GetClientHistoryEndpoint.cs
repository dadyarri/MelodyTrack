using Facet.Mapping;
using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.ErrorHandling;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class GetClientHistoryEndpoint(AppDbContext db, ClientToClientWithBalanceDtoMapConfig mapper)
    : Ep.Req<GetEntityRequest>.Res<Results<Ok<ClientHistoryResponse>, NotFound<ProblemDetails>>>
{
    public override void Configure()
    {
        Get("/clients/{id}/history");
    }

    public override async Task<Results<Ok<ClientHistoryResponse>, NotFound<ProblemDetails>>> ExecuteAsync(
        GetEntityRequest req,
        CancellationToken ct)
    {
        Logger.LogDebug("Fetching history for client {ClientId}", req.Id);

        var client = await db.Clients
            .AsNoTracking()
            .Include(e => e.Contacts)
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

        var recentPayments = await db.Payments
            .AsNoTracking()
            .Where(e => e.Client.Id == client.Id)
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.Id)
            .Take(8)
            .Select(e => new ClientHistoryPaymentDto
            {
                Id = e.Id,
                Amount = e.Amount,
                Date = e.Date,
                Description = e.Description,
                ServiceName = e.Service != null ? e.Service.Name : null
            })
            .ToListAsync(ct);

        var recentAppointments = await db.Appointments
            .AsNoTracking()
            .Where(e => e.Client.Id == client.Id && !e.IsDeleted)
            .Include(e => e.Service)
            .Include(e => e.Provider)
                .ThenInclude(e => e!.Role)
            .OrderByDescending(e => e.StartDate)
            .ThenByDescending(e => e.Id)
            .Take(8)
            .Select(e => new ClientHistoryAppointmentDto
            {
                Id = e.Id,
                StartDate = e.StartDate,
                EndDate = e.EndDate,
                ServiceName = e.Service.Name,
                ProviderDisplayName = e.Provider == null
                    ? null
                    : e.Provider.FirstName + " " + e.Provider.LastName,
                IsCompleted = e.IsCompleted,
                IsCanceled = e.IsCanceled
            })
            .ToListAsync(ct);

        var totalPayments = await db.Payments
            .AsNoTracking()
            .Where(e => e.Client.Id == client.Id)
            .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;

        var paymentsCount = await db.Payments
            .AsNoTracking()
            .CountAsync(e => e.Client.Id == client.Id, ct);

        var completedAppointmentsQuery = db.Appointments
            .AsNoTracking()
            .Where(e => e.Client.Id == client.Id && e.IsCompleted && !e.IsCanceled && !e.IsDeleted);

        var completedAppointmentsCount = await completedAppointmentsQuery.CountAsync(ct);
        var lastVisitAtUtc = await completedAppointmentsQuery
            .OrderByDescending(e => e.StartDate)
            .Select(e => (DateTime?)e.StartDate)
            .FirstOrDefaultAsync(ct);

        var upcomingAppointmentsQuery = db.Appointments
            .AsNoTracking()
            .Where(e => e.Client.Id == client.Id && !e.IsCanceled && !e.IsDeleted && e.StartDate >= DateTime.UtcNow);

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
            RecentPayments = recentPayments,
            RecentAppointments = recentAppointments
        });
    }
}
