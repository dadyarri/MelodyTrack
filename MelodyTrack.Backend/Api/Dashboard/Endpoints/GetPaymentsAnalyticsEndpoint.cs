using FastEndpoints;
using MelodyTrack.Backend.Api.Dashboard;
using MelodyTrack.Backend.Api.Dashboard.Requests;
using MelodyTrack.Backend.Api.Dashboard.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Dashboard.Endpoints;

public class GetPaymentsAnalyticsEndpoint(AppDbContext db)
    : Ep.Req<GetPaymentsAnalyticsRequest>.Res<Results<Ok<GetPaymentsAnalyticsResponse>, UnauthorizedHttpResult, ForbidHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/dashboard/payments");
    }

    public override async Task<Results<Ok<GetPaymentsAnalyticsResponse>, UnauthorizedHttpResult, ForbidHttpResult, ProblemDetails>> ExecuteAsync(
        GetPaymentsAnalyticsRequest req,
        CancellationToken ct)
    {
        var currentUser = await DashboardAccess.GetCurrentUserAsync(User, db, ct);

        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!DashboardAccess.CanViewDashboardAnalytics(currentUser))
        {
            return TypedResults.Forbid();
        }

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
            .Where(e => !e.IsDeleted && (e.Status == AppointmentStatus.Completed || e.Status == AppointmentStatus.Burned))
            .Select(e => new AppointmentRow
            {
                AppointmentId = e.Id,
                ClientId = e.Client.Id,
                ClientFirstName = e.Client.FirstName,
                ClientLastName = e.Client.LastName,
                ServiceId = e.Service.Id,
                ServiceName = e.Service.Name,
                ProviderId = e.Provider != null ? e.Provider.Id : null,
                ProviderFirstName = e.Provider != null ? e.Provider.FirstName : null,
                ProviderLastName = e.Provider != null ? e.Provider.LastName : null,
                StartDate = e.StartDate
            })
            .ToListAsync(ct);

        var payments = await db.Payments
            .AsNoTracking()
            .Select(e => new PaymentRow
            {
                PaymentId = e.Id,
                ClientId = e.Client.Id,
                ServiceId = e.Service != null ? e.Service.Id : null,
                Date = e.Date,
                Amount = e.Amount
            })
            .ToListAsync(ct);

        var serviceIds = appointments.Select(e => e.ServiceId).Distinct().ToList();
        var latestRelevantDateUtc = new[]
        {
            appointments.Select(e => (DateTime?)e.StartDate).DefaultIfEmpty().Max(),
            payments.Select(e => (DateTime?)e.Date).DefaultIfEmpty().Max()
        }.Max() ?? DateTime.UtcNow;

        var servicePrices = await db.ServicePriceHistory
            .AsNoTracking()
            .Where(e => serviceIds.Contains(e.Service.Id) && e.EffectiveDate <= latestRelevantDateUtc)
            .Select(e => new PriceRow
            {
                ServiceId = e.Service.Id,
                EffectiveDate = e.EffectiveDate,
                Price = e.Price
            })
            .ToListAsync(ct);

        var priceLookup = servicePrices
            .GroupBy(e => e.ServiceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(e => e.EffectiveDate).ToList());

        var appointmentLedgers = appointments
            .Select(e => new AppointmentLedger
            {
                AppointmentId = e.AppointmentId,
                ClientId = e.ClientId,
                ClientDisplayName = $"{e.ClientLastName} {e.ClientFirstName}".Trim(),
                ServiceId = e.ServiceId,
                ServiceName = e.ServiceName,
                TeacherId = e.ProviderId,
                TeacherDisplayName = e.ProviderId is null ? "Без преподавателя" : $"{e.ProviderLastName} {e.ProviderFirstName}".Trim(),
                StartDate = e.StartDate,
                Amount = DashboardPriceResolver.ResolveAppointmentPrice(
                    e.ServiceId,
                    e.StartDate,
                    priceLookup,
                    price => price.EffectiveDate,
                    price => price.Price),
                RemainingAmount = DashboardPriceResolver.ResolveAppointmentPrice(
                    e.ServiceId,
                    e.StartDate,
                    priceLookup,
                    price => price.EffectiveDate,
                    price => price.Price)
            })
            .OrderBy(e => e.ClientId)
            .ThenBy(e => e.StartDate)
            .ThenBy(e => e.AppointmentId)
            .ToList();

        var paymentsByClient = payments
            .GroupBy(e => e.ClientId)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(e => e.Date)
                    .ThenBy(e => e.PaymentId)
                    .ToList());

        var appointmentsByClient = appointmentLedgers
            .GroupBy(e => e.ClientId)
            .ToDictionary(group => group.Key, group => group.ToList());

        var allocations = new List<AllocationRow>();

        foreach (var (clientId, clientAppointments) in appointmentsByClient)
        {
            if (!paymentsByClient.TryGetValue(clientId, out var clientPayments))
            {
                continue;
            }

            foreach (var payment in clientPayments)
            {
                var remaining = payment.Amount;

                if (payment.ServiceId is { } serviceId)
                {
                    remaining = AllocatePayment(
                        clientAppointments.Where(e => e.ServiceId == serviceId).ToList(),
                        payment,
                        remaining,
                        allocations,
                        timezone);
                }

                if (remaining > 0)
                {
                    _ = AllocatePayment(clientAppointments, payment, remaining, allocations, timezone);
                }
            }
        }

        var delayAllocations = allocations
            .Where(e => e.PaymentDate >= rangeStartUtc && e.PaymentDate < rangeEndExclusiveUtc)
            .ToList();

        var clientDtos = appointmentLedgers
            .GroupBy(e => new { e.ClientId, e.ClientDisplayName })
            .Select(group =>
            {
                var groupAllocations = delayAllocations.Where(e => e.ClientId == group.Key.ClientId).ToList();
                var totalRevenue = group.Sum(e => e.Amount);
                var totalPayments = payments.Where(e => e.ClientId == group.Key.ClientId).Sum(e => e.Amount);
                var debt = group.Sum(e => e.RemainingAmount);
                var delays = groupAllocations.Select(e => e.DelayDays).ToList();

                return new ClientPaymentsAnalyticsDto
                {
                    ClientId = group.Key.ClientId,
                    ClientDisplayName = group.Key.ClientDisplayName,
                    TotalRevenue = totalRevenue,
                    TotalPayments = totalPayments,
                    Balance = totalPayments - totalRevenue,
                    Debt = debt,
                    UnpaidAppointmentsCount = group.Count(e => e.RemainingAmount > 0),
                    AveragePaymentDelayDays = CalculateAverage(delays),
                    MedianPaymentDelayDays = CalculateMedian(delays),
                    MaxPaymentDelayDays = CalculateMax(delays)
                };
            })
            .OrderByDescending(e => e.Debt)
            .ThenBy(e => e.ClientDisplayName)
            .ToList();

        var teacherDtos = appointmentLedgers
            .GroupBy(e => new { e.TeacherId, e.TeacherDisplayName })
            .Select(group =>
            {
                var appointmentIds = group.Select(e => e.AppointmentId).ToHashSet();
                var groupAllocations = delayAllocations.Where(e => appointmentIds.Contains(e.AppointmentId)).ToList();
                var delays = groupAllocations.Select(e => e.DelayDays).ToList();

                return new TeacherPaymentsAnalyticsDto
                {
                    TeacherId = group.Key.TeacherId,
                    TeacherDisplayName = group.Key.TeacherDisplayName,
                    TotalRevenue = group.Sum(e => e.Amount),
                    OutstandingDebt = group.Sum(e => e.RemainingAmount),
                    UnpaidAppointmentsCount = group.Count(e => e.RemainingAmount > 0),
                    AveragePaymentDelayDays = CalculateAverage(delays),
                    MedianPaymentDelayDays = CalculateMedian(delays),
                    MaxPaymentDelayDays = CalculateMax(delays)
                };
            })
            .OrderByDescending(e => e.OutstandingDebt)
            .ThenBy(e => e.TeacherDisplayName)
            .ToList();

        var serviceDtos = appointmentLedgers
            .GroupBy(e => new { e.ServiceId, e.ServiceName })
            .Select(group =>
            {
                var appointmentIds = group.Select(e => e.AppointmentId).ToHashSet();
                var groupAllocations = delayAllocations.Where(e => appointmentIds.Contains(e.AppointmentId)).ToList();
                var delays = groupAllocations.Select(e => e.DelayDays).ToList();

                return new ServicePaymentsAnalyticsDto
                {
                    ServiceId = group.Key.ServiceId,
                    ServiceName = group.Key.ServiceName,
                    TotalRevenue = group.Sum(e => e.Amount),
                    OutstandingDebt = group.Sum(e => e.RemainingAmount),
                    UnpaidAppointmentsCount = group.Count(e => e.RemainingAmount > 0),
                    AveragePaymentDelayDays = CalculateAverage(delays),
                    MedianPaymentDelayDays = CalculateMedian(delays),
                    MaxPaymentDelayDays = CalculateMax(delays)
                };
            })
            .OrderByDescending(e => e.OutstandingDebt)
            .ThenBy(e => e.ServiceName)
            .ToList();

        var globalDelays = delayAllocations.Select(e => e.DelayDays).ToList();

        return TypedResults.Ok(new GetPaymentsAnalyticsResponse
        {
            StartDate = rangeStartLocal,
            EndDate = req.End.Date,
            UnpaidAppointmentsCount = appointmentLedgers.Count(e => e.RemainingAmount > 0),
            DebtorsCount = clientDtos.Count(e => e.Debt > 0),
            TotalDebt = appointmentLedgers.Sum(e => e.RemainingAmount),
            AveragePaymentDelayDays = CalculateAverage(globalDelays),
            MedianPaymentDelayDays = CalculateMedian(globalDelays),
            MaxPaymentDelayDays = CalculateMax(globalDelays),
            Clients = clientDtos,
            Teachers = teacherDtos,
            Services = serviceDtos
        });
    }

    private static decimal AllocatePayment(
        List<AppointmentLedger> openAppointments,
        PaymentRow payment,
        decimal remaining,
        List<AllocationRow> allocations,
        TimeZoneInfo timezone)
    {
        foreach (var appointment in openAppointments.Where(e => e.RemainingAmount > 0).OrderBy(e => e.StartDate).ThenBy(e => e.AppointmentId))
        {
            if (remaining <= 0)
            {
                break;
            }

            var allocatedAmount = Math.Min(appointment.RemainingAmount, remaining);
            if (allocatedAmount <= 0)
            {
                continue;
            }

            appointment.RemainingAmount -= allocatedAmount;
            remaining -= allocatedAmount;
            allocations.Add(new AllocationRow
            {
                PaymentId = payment.PaymentId,
                ClientId = appointment.ClientId,
                AppointmentId = appointment.AppointmentId,
                ServiceId = appointment.ServiceId,
                TeacherId = appointment.TeacherId,
                PaymentDate = payment.Date,
                DelayDays = CalculateDelayDays(payment.Date, appointment.StartDate, timezone),
                Amount = allocatedAmount
            });
        }

        return remaining;
    }

    private static decimal CalculateDelayDays(DateTime paymentDateUtc, DateTime appointmentStartDateUtc, TimeZoneInfo timezone)
    {
        var paymentLocalDate = TimeZoneInfo.ConvertTimeFromUtc(paymentDateUtc, timezone).Date;
        var appointmentLocalDate = TimeZoneInfo.ConvertTimeFromUtc(appointmentStartDateUtc, timezone).Date;
        return Math.Max(0m, (decimal)(paymentLocalDate - appointmentLocalDate).TotalDays);
    }

    private static decimal? CalculateAverage(List<decimal> values)
    {
        return values.Count == 0 ? null : values.Average();
    }

    private static decimal? CalculateMedian(List<decimal> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var ordered = values.OrderBy(e => e).ToList();
        var middle = ordered.Count / 2;
        return ordered.Count % 2 == 0
            ? (ordered[middle - 1] + ordered[middle]) / 2m
            : ordered[middle];
    }

    private static decimal? CalculateMax(List<decimal> values)
    {
        return values.Count == 0 ? null : values.Max();
    }

    private sealed class AppointmentRow
    {
        public required Ulid AppointmentId { get; set; }
        public required Ulid ClientId { get; set; }
        public required string ClientFirstName { get; set; }
        public required string ClientLastName { get; set; }
        public required Ulid ServiceId { get; set; }
        public required string ServiceName { get; set; }
        public Ulid? ProviderId { get; set; }
        public string? ProviderFirstName { get; set; }
        public string? ProviderLastName { get; set; }
        public required DateTime StartDate { get; set; }
    }

    private sealed class PaymentRow
    {
        public required Ulid PaymentId { get; set; }
        public required Ulid ClientId { get; set; }
        public Ulid? ServiceId { get; set; }
        public required DateTime Date { get; set; }
        public required decimal Amount { get; set; }
    }

    private sealed class PriceRow
    {
        public required Ulid ServiceId { get; set; }
        public required DateTime EffectiveDate { get; set; }
        public required decimal Price { get; set; }
    }

    private sealed class AppointmentLedger
    {
        public required Ulid AppointmentId { get; set; }
        public required Ulid ClientId { get; set; }
        public required string ClientDisplayName { get; set; }
        public required Ulid ServiceId { get; set; }
        public required string ServiceName { get; set; }
        public Ulid? TeacherId { get; set; }
        public required string TeacherDisplayName { get; set; }
        public required DateTime StartDate { get; set; }
        public required decimal Amount { get; set; }
        public required decimal RemainingAmount { get; set; }
    }

    private sealed class AllocationRow
    {
        public required Ulid PaymentId { get; set; }
        public required Ulid ClientId { get; set; }
        public required Ulid AppointmentId { get; set; }
        public required Ulid ServiceId { get; set; }
        public Ulid? TeacherId { get; set; }
        public required DateTime PaymentDate { get; set; }
        public required decimal DelayDays { get; set; }
        public required decimal Amount { get; set; }
    }
}
