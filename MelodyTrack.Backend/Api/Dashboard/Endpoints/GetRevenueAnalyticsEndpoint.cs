using FastEndpoints;
using MelodyTrack.Backend.Api.Dashboard.Requests;
using MelodyTrack.Backend.Api.Dashboard.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Dashboard.Endpoints;

public class GetRevenueAnalyticsEndpoint(AppDbContext db, IRecurringAppointmentMaterializer recurringAppointmentMaterializer)
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

        if (!TryParseGroupBy(req.GroupBy, out var groupBy))
        {
            AddError(r => r.GroupBy, "Некорректная группировка. Доступно: day, week, month, year.");
            return new ProblemDetails(ValidationFailures);
        }

        var rangeStartLocal = req.Start.Date;
        var rangeEndExclusiveLocal = req.End.Date.AddDays(1);
        var rangeStartUtc = TimeZoneInfo.ConvertTimeToUtc(rangeStartLocal, timezone);
        var rangeEndExclusiveUtc = TimeZoneInfo.ConvertTimeToUtc(rangeEndExclusiveLocal, timezone);

        await recurringAppointmentMaterializer.EnsureAppointmentsGeneratedAsync(rangeStartUtc, rangeEndExclusiveUtc.AddTicks(-1), ct);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Where(e => !e.IsDeleted && e.StartDate >= rangeStartUtc && e.StartDate < rangeEndExclusiveUtc)
            .Select(e => new RevenueAppointmentRow
            {
                Status = e.Status,
                ServiceId = e.Service.Id,
                ServiceName = e.Service.Name,
                ClientId = e.Client.Id,
                ClientFirstName = e.Client.FirstName,
                ClientLastName = e.Client.LastName,
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
            ServiceName = appointment.ServiceName,
            ClientId = appointment.ClientId,
            ClientDisplayName = $"{appointment.ClientLastName} {appointment.ClientFirstName}".Trim(),
            StartDate = appointment.StartDate,
            ProviderId = appointment.ProviderId,
            ProviderDisplayName = appointment.ProviderId is null
                ? "Без преподавателя"
                : $"{appointment.ProviderLastName} {appointment.ProviderFirstName}".Trim(),
            Price = ResolveAppointmentPrice(appointment.ServiceId, appointment.StartDate, priceLookup),
            LocalStartDate = TimeZoneInfo.ConvertTimeFromUtc(appointment.StartDate, timezone)
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

        var clients = revenueAppointments
            .GroupBy(e => new { e.ClientId, e.ClientDisplayName })
            .Select(group => new ClientRevenueAnalyticsDto
            {
                ClientId = group.Key.ClientId,
                ClientDisplayName = group.Key.ClientDisplayName,
                Revenue = group.Sum(e => e.Price),
                RevenueShare = totalRevenue == 0 ? null : group.Sum(e => e.Price) / totalRevenue * 100m,
                AverageReceipt = group.Any() ? group.Average(e => e.Price) : null,
                RevenueCountedAppointmentsCount = group.Count()
            })
            .OrderByDescending(e => e.Revenue)
            .ThenBy(e => e.ClientDisplayName)
            .ToList();

        var services = revenueAppointments
            .GroupBy(e => new { e.ServiceId, e.ServiceName })
            .Select(group => new ServiceRevenueAnalyticsDto
            {
                ServiceId = group.Key.ServiceId,
                ServiceName = group.Key.ServiceName,
                Revenue = group.Sum(e => e.Price),
                RevenueShare = totalRevenue == 0 ? null : group.Sum(e => e.Price) / totalRevenue * 100m,
                AverageReceipt = group.Any() ? group.Average(e => e.Price) : null,
                RevenueCountedAppointmentsCount = group.Count(),
                CompletedAppointmentsCount = group.Count(e => e.Status == AppointmentStatus.Completed),
                BurnedAppointmentsCount = group.Count(e => e.Status == AppointmentStatus.Burned)
            })
            .OrderByDescending(e => e.Revenue)
            .ThenBy(e => e.ServiceName)
            .ToList();

        var expenses = (await db.Expenses
            .AsNoTracking()
            .Where(e => e.Date >= rangeStartUtc && e.Date < rangeEndExclusiveUtc)
            .Select(e => new RevenueExpenseUtcRow
            {
                UtcDate = e.Date,
                Amount = e.Amount
            })
            .ToListAsync(ct))
            .Select(e => new RevenueExpenseRow
            {
                LocalDate = TimeZoneInfo.ConvertTimeFromUtc(e.UtcDate, timezone),
                Amount = e.Amount
            })
            .ToList();

        var bucketStarts = BuildBucketStarts(rangeStartLocal, rangeEndExclusiveLocal, groupBy);
        var revenueByBucket = revenueAppointments
            .GroupBy(e => GetBucketStart(e.LocalStartDate.Date, groupBy))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Price));
        var expensesByBucket = expenses
            .GroupBy(e => GetBucketStart(e.LocalDate.Date, groupBy))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

        decimal? previousNetProfit = null;
        var dynamics = bucketStarts
            .Select(bucketStart =>
            {
                var bucketEnd = GetBucketEndExclusive(bucketStart, groupBy).AddDays(-1);
                var revenue = revenueByBucket.GetValueOrDefault(bucketStart, 0m);
                var bucketExpenses = expensesByBucket.GetValueOrDefault(bucketStart, 0m);
                var netProfit = revenue - bucketExpenses;
                var changeFromPrevious = previousNetProfit is null ? (decimal?)null : netProfit - previousNetProfit.Value;
                var changePercentFromPrevious = previousNetProfit is null || previousNetProfit == 0
                    ? (decimal?)null
                    : (netProfit - previousNetProfit.Value) / Math.Abs(previousNetProfit.Value) * 100m;
                previousNetProfit = netProfit;

                return new NetProfitBucketDto
                {
                    StartDate = bucketStart,
                    EndDate = bucketEnd,
                    Revenue = revenue,
                    Expenses = bucketExpenses,
                    NetProfit = netProfit,
                    ChangeFromPrevious = changeFromPrevious,
                    ChangePercentFromPrevious = changePercentFromPrevious,
                    LossPercentageRelativeToRevenue = netProfit < 0
                        ? (revenue == 0 ? null : Math.Abs(netProfit) / revenue * 100m)
                        : null
                };
            })
            .ToList();

        return TypedResults.Ok(new GetRevenueAnalyticsResponse
        {
            StartDate = rangeStartLocal,
            EndDate = req.End.Date,
            GroupBy = ToApiKey(groupBy),
            TotalRevenue = totalRevenue,
            PlannedRevenue = plannedRevenue,
            TotalExpenses = totalExpenses,
            NetProfit = totalRevenue - totalExpenses,
            AverageReceipt = revenueAppointments.Count == 0 ? null : totalRevenue / revenueAppointments.Count,
            RevenueCountedAppointmentsCount = revenueAppointments.Count,
            PlannedAppointmentsCount = plannedAppointments.Count,
            Teachers = teachers,
            Clients = clients,
            Services = services,
            NetProfitDynamics = dynamics,
            MostProfitablePeriods = dynamics
                .Where(e => e.NetProfit > 0)
                .OrderByDescending(e => e.NetProfit)
                .ThenBy(e => e.StartDate)
                .Take(5)
                .ToList(),
            UnprofitablePeriods = dynamics
                .Where(e => e.NetProfit < 0)
                .OrderBy(e => e.NetProfit)
                .ThenBy(e => e.StartDate)
                .Take(5)
                .ToList()
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

    private static bool TryParseGroupBy(string? value, out RevenueGroupBy groupBy)
    {
        groupBy = RevenueGroupBy.Month;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "day":
                groupBy = RevenueGroupBy.Day;
                return true;
            case "week":
                groupBy = RevenueGroupBy.Week;
                return true;
            case "month":
                groupBy = RevenueGroupBy.Month;
                return true;
            case "year":
                groupBy = RevenueGroupBy.Year;
                return true;
            default:
                return false;
        }
    }

    private static List<DateTime> BuildBucketStarts(DateTime rangeStartLocal, DateTime rangeEndExclusiveLocal, RevenueGroupBy groupBy)
    {
        var bucketStart = GetBucketStart(rangeStartLocal, groupBy);
        var result = new List<DateTime>();

        while (bucketStart < rangeEndExclusiveLocal)
        {
            result.Add(bucketStart);
            bucketStart = GetBucketEndExclusive(bucketStart, groupBy);
        }

        return result;
    }

    private static DateTime GetBucketStart(DateTime date, RevenueGroupBy groupBy)
    {
        return groupBy switch
        {
            RevenueGroupBy.Day => date.Date,
            RevenueGroupBy.Week => date.Date.AddDays(-GetMondayOffset(date.DayOfWeek)),
            RevenueGroupBy.Month => new DateTime(date.Year, date.Month, 1),
            RevenueGroupBy.Year => new DateTime(date.Year, 1, 1),
            _ => date.Date
        };
    }

    private static DateTime GetBucketEndExclusive(DateTime bucketStart, RevenueGroupBy groupBy)
    {
        return groupBy switch
        {
            RevenueGroupBy.Day => bucketStart.AddDays(1),
            RevenueGroupBy.Week => bucketStart.AddDays(7),
            RevenueGroupBy.Month => bucketStart.AddMonths(1),
            RevenueGroupBy.Year => bucketStart.AddYears(1),
            _ => bucketStart.AddDays(1)
        };
    }

    private static int GetMondayOffset(DayOfWeek dayOfWeek)
    {
        return dayOfWeek switch
        {
            DayOfWeek.Monday => 0,
            DayOfWeek.Tuesday => 1,
            DayOfWeek.Wednesday => 2,
            DayOfWeek.Thursday => 3,
            DayOfWeek.Friday => 4,
            DayOfWeek.Saturday => 5,
            DayOfWeek.Sunday => 6,
            _ => 0
        };
    }

    private enum RevenueGroupBy
    {
        Day,
        Week,
        Month,
        Year
    }

    private static string ToApiKey(RevenueGroupBy groupBy)
    {
        return groupBy switch
        {
            RevenueGroupBy.Day => "day",
            RevenueGroupBy.Week => "week",
            RevenueGroupBy.Year => "year",
            _ => "month"
        };
    }

    private sealed class RevenueAppointmentRow
    {
        public required AppointmentStatus Status { get; set; }
        public required Ulid ServiceId { get; set; }
        public required string ServiceName { get; set; }
        public required Ulid ClientId { get; set; }
        public required string ClientFirstName { get; set; }
        public required string ClientLastName { get; set; }
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

    private sealed class RevenueExpenseUtcRow
    {
        public required DateTime UtcDate { get; set; }
        public required decimal Amount { get; set; }
    }

    private sealed class RevenueExpenseRow
    {
        public required DateTime LocalDate { get; set; }
        public required decimal Amount { get; set; }
    }

    private sealed class RevenueAppointmentValue
    {
        public required AppointmentStatus Status { get; set; }
        public required Ulid ServiceId { get; set; }
        public required string ServiceName { get; set; }
        public required Ulid ClientId { get; set; }
        public required string ClientDisplayName { get; set; }
        public required DateTime StartDate { get; set; }
        public required DateTime LocalStartDate { get; set; }
        public Ulid? ProviderId { get; set; }
        public required string ProviderDisplayName { get; set; }
        public required decimal Price { get; set; }
    }
}
