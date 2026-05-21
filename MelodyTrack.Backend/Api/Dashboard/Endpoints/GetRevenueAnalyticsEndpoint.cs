using FastEndpoints;
using MelodyTrack.Backend.Api.Dashboard.Requests;
using MelodyTrack.Backend.Api.Dashboard.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Dashboard.Endpoints;

public class GetRevenueAnalyticsEndpoint(AppDbContext db)
    : Ep.Req<GetRevenueAnalyticsRequest>.Res<Results<Ok<GetRevenueAnalyticsResponse>, UnauthorizedHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/dashboard/revenue");
    }

    public override async Task<Results<Ok<GetRevenueAnalyticsResponse>, UnauthorizedHttpResult, ProblemDetails>> ExecuteAsync(
        GetRevenueAnalyticsRequest req,
        CancellationToken ct)
    {
        TimeZoneInfo timezone;
        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(req.Timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            AddError(r => r.Timezone, "Часовой пояс не найден");
            return new ProblemDetails(ValidationFailures);
        }
        catch (InvalidTimeZoneException)
        {
            AddError(r => r.Timezone, "Часовой пояс недоступен");
            return new ProblemDetails(ValidationFailures);
        }

        if (req.End < req.Start)
        {
            AddError(r => r.End, "Дата окончания не может быть раньше даты начала.");
            return new ProblemDetails(ValidationFailures);
        }

        var rangeStartLocal = req.Start.Date;
        var rangeEndExclusiveLocal = req.End.Date.AddDays(1);
        var rangeStartUtc = TimeZoneInfo.ConvertTimeToUtc(rangeStartLocal, timezone);
        var rangeEndExclusiveUtc = TimeZoneInfo.ConvertTimeToUtc(rangeEndExclusiveLocal, timezone);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Where(e => !e.IsDeleted && e.StartDate >= rangeStartUtc && e.StartDate < rangeEndExclusiveUtc)
            .Select(e => new RevenueAppointmentRow
            {
                Status = e.Status,
                ServiceId = e.Service.Id,
                StartDate = e.StartDate,
                ProviderId = e.Provider != null ? e.Provider.Id : null,
                ProviderFirstName = e.Provider != null ? e.Provider.FirstName : null,
                ProviderLastName = e.Provider != null ? e.Provider.LastName : null
            })
            .ToListAsync(ct);

        var serviceIds = appointments.Select(e => e.ServiceId).Distinct().ToList();
        var servicePrices = await db.ServicePriceHistory
            .AsNoTracking()
            .Where(e => serviceIds.Contains(e.Service.Id) && e.EffectiveDate < rangeEndExclusiveUtc)
            .Select(e => new RevenuePriceRow
            {
                ServiceId = e.Service.Id,
                EffectiveDate = e.EffectiveDate,
                Price = e.Price
            })
            .ToListAsync(ct);

        var priceLookup = servicePrices
            .GroupBy(e => e.ServiceId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.EffectiveDate).ToList());

        var appointmentValues = appointments.Select(appointment => new RevenueAppointmentValue
        {
            Status = appointment.Status,
            ServiceId = appointment.ServiceId,
            StartDate = appointment.StartDate,
            ProviderId = appointment.ProviderId,
            ProviderDisplayName = appointment.ProviderId is null
                ? "Без преподавателя"
                : $"{appointment.ProviderLastName} {appointment.ProviderFirstName}".Trim(),
            Price = ResolveAppointmentPrice(appointment.ServiceId, appointment.StartDate, priceLookup)
        }).ToList();

        var revenueAppointments = appointmentValues
            .Where(e => e.Status.CountsAsRevenue())
            .ToList();

        var plannedAppointments = appointmentValues
            .Where(e => e.Status == AppointmentStatus.Planned)
            .ToList();

        var totalRevenue = revenueAppointments.Sum(e => e.Price);
        var plannedRevenue = plannedAppointments.Sum(e => e.Price);
        var totalExpenses = await db.Expenses
            .AsNoTracking()
            .Where(e => e.Date >= rangeStartUtc && e.Date < rangeEndExclusiveUtc)
            .SumAsync(e => e.Amount, ct);

        var teachers = revenueAppointments
            .GroupBy(e => new { e.ProviderId, e.ProviderDisplayName })
            .Select(group => new TeacherRevenueAnalyticsDto
            {
                TeacherId = group.Key.ProviderId,
                TeacherDisplayName = group.Key.ProviderDisplayName,
                Revenue = group.Sum(e => e.Price),
                RevenueShare = totalRevenue == 0 ? null : group.Sum(e => e.Price) / totalRevenue * 100m,
                AverageReceipt = group.Any() ? group.Average(e => e.Price) : null,
                RevenueCountedAppointmentsCount = group.Count(),
                CompletedAppointmentsCount = group.Count(e => e.Status == AppointmentStatus.Completed),
                BurnedAppointmentsCount = group.Count(e => e.Status == AppointmentStatus.Burned),
                ServicesProvidedCount = group.Count(e => e.Status == AppointmentStatus.Completed)
            })
            .OrderByDescending(e => e.Revenue)
            .ThenBy(e => e.TeacherDisplayName)
            .ToList();

        return TypedResults.Ok(new GetRevenueAnalyticsResponse
        {
            StartDate = rangeStartLocal,
            EndDate = req.End.Date,
            TotalRevenue = totalRevenue,
            PlannedRevenue = plannedRevenue,
            TotalExpenses = totalExpenses,
            NetProfit = totalRevenue - totalExpenses,
            AverageReceipt = revenueAppointments.Count == 0 ? null : totalRevenue / revenueAppointments.Count,
            RevenueCountedAppointmentsCount = revenueAppointments.Count,
            PlannedAppointmentsCount = plannedAppointments.Count,
            Teachers = teachers
        });
    }

    private static decimal ResolveAppointmentPrice(
        Ulid serviceId,
        DateTime appointmentStartDate,
        IReadOnlyDictionary<Ulid, List<RevenuePriceRow>> priceLookup)
    {
        if (!priceLookup.TryGetValue(serviceId, out var prices))
        {
            return 0m;
        }

        return prices
            .Where(price => price.EffectiveDate <= appointmentStartDate)
            .Select(price => price.Price)
            .FirstOrDefault();
    }

    private sealed class RevenueAppointmentRow
    {
        public required AppointmentStatus Status { get; set; }
        public required Ulid ServiceId { get; set; }
        public required DateTime StartDate { get; set; }
        public Ulid? ProviderId { get; set; }
        public string? ProviderFirstName { get; set; }
        public string? ProviderLastName { get; set; }
    }

    private sealed class RevenuePriceRow
    {
        public required Ulid ServiceId { get; set; }
        public required DateTime EffectiveDate { get; set; }
        public required decimal Price { get; set; }
    }

    private sealed class RevenueAppointmentValue
    {
        public required AppointmentStatus Status { get; set; }
        public required Ulid ServiceId { get; set; }
        public required DateTime StartDate { get; set; }
        public Ulid? ProviderId { get; set; }
        public required string ProviderDisplayName { get; set; }
        public required decimal Price { get; set; }
    }
}
