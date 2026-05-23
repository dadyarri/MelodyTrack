using FastEndpoints;
using MelodyTrack.Backend.Api.Dashboard;
using MelodyTrack.Backend.Api.Dashboard.Requests;
using MelodyTrack.Backend.Api.Dashboard.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Dashboard.Endpoints;

public class GetExpensesAnalyticsEndpoint(AppDbContext db, IRecurringAppointmentMaterializer recurringAppointmentMaterializer)
    : Ep.Req<GetExpensesAnalyticsRequest>.Res<Results<Ok<GetExpensesAnalyticsResponse>, UnauthorizedHttpResult, ForbidHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/dashboard/expenses");
    }

    public override async Task<Results<Ok<GetExpensesAnalyticsResponse>, UnauthorizedHttpResult, ForbidHttpResult, ProblemDetails>> ExecuteAsync(
        GetExpensesAnalyticsRequest req,
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

        var expenses = (await db.Expenses
            .AsNoTracking()
            .Where(e => e.Date >= rangeStartUtc && e.Date < rangeEndExclusiveUtc)
            .Select(e => new ExpenseRow
            {
                CategoryId = e.CategoryId,
                CategoryName = e.Category != null ? e.Category.Name : null,
                LocalDate = TimeZoneInfo.ConvertTimeFromUtc(e.Date, timezone),
                Amount = e.Amount
            })
            .ToListAsync(ct));

        var revenueAppointments = await db.Appointments
            .AsNoTracking()
            .Where(e => !e.IsDeleted
                        && (e.Status == AppointmentStatus.Completed || e.Status == AppointmentStatus.Burned)
                        && e.StartDate >= rangeStartUtc
                        && e.StartDate < rangeEndExclusiveUtc)
            .Select(e => new RevenueAppointmentRow
            {
                ServiceId = e.Service.Id,
                StartDate = e.StartDate
            })
            .ToListAsync(ct);

        var serviceIds = revenueAppointments.Select(e => e.ServiceId).Distinct().ToList();
        var servicePrices = await db.ServicePriceHistory
            .AsNoTracking()
            .Where(e => serviceIds.Contains(e.Service.Id) && e.EffectiveDate < rangeEndExclusiveUtc)
            .Select(e => new ServicePriceRow
            {
                ServiceId = e.Service.Id,
                EffectiveDate = e.EffectiveDate,
                Price = e.Price
            })
            .ToListAsync(ct);

        var priceLookup = servicePrices
            .GroupBy(e => e.ServiceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(e => e.EffectiveDate).ToList());

        var totalExpenses = expenses.Sum(e => e.Amount);
        var totalRevenue = revenueAppointments.Sum(e => ResolveAppointmentPrice(e.ServiceId, e.StartDate, priceLookup));

        var categories = expenses
            .GroupBy(e => new
            {
                e.CategoryId,
                CategoryName = string.IsNullOrWhiteSpace(e.CategoryName) ? "Без категории" : e.CategoryName
            })
            .Select(group => new ExpenseCategoryAnalyticsDto
            {
                CategoryId = group.Key.CategoryId,
                CategoryName = group.Key.CategoryName!,
                Amount = group.Sum(e => e.Amount),
                Share = totalExpenses == 0 ? null : group.Sum(e => e.Amount) / totalExpenses * 100m
            })
            .OrderByDescending(e => e.Amount)
            .ThenBy(e => e.CategoryName)
            .ToList();

        var bucketStarts = BuildBucketStarts(rangeStartLocal, rangeEndExclusiveLocal, groupBy);
        var expensesByBucket = expenses
            .GroupBy(e => GetBucketStart(e.LocalDate.Date, groupBy))
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

        decimal? previousExpenses = null;
        var dynamics = bucketStarts
            .Select(bucketStart =>
            {
                var bucketExpenses = expensesByBucket.GetValueOrDefault(bucketStart, 0m);
                var changeFromPrevious = previousExpenses is null ? (decimal?)null : bucketExpenses - previousExpenses.Value;
                var changePercentFromPrevious = CalculateChangePercentFromPrevious(previousExpenses, bucketExpenses);
                previousExpenses = bucketExpenses;

                return new ExpenseDynamicsBucketDto
                {
                    StartDate = bucketStart,
                    EndDate = GetBucketEndExclusive(bucketStart, groupBy).AddDays(-1),
                    Expenses = bucketExpenses,
                    ChangeFromPrevious = changeFromPrevious,
                    ChangePercentFromPrevious = changePercentFromPrevious
                };
            })
            .ToList();

        return TypedResults.Ok(new GetExpensesAnalyticsResponse
        {
            StartDate = rangeStartLocal,
            EndDate = req.End.Date,
            GroupBy = ToApiKey(groupBy),
            TotalExpenses = totalExpenses,
            TotalRevenue = totalRevenue,
            ExpenseToRevenueRatio = totalRevenue == 0 ? null : totalExpenses / totalRevenue * 100m,
            ExpensesCount = expenses.Count,
            Categories = categories,
            Dynamics = dynamics
        });
    }

    private static decimal ResolveAppointmentPrice(
        Ulid serviceId,
        DateTime appointmentStartDate,
        IReadOnlyDictionary<Ulid, List<ServicePriceRow>> priceLookup)
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

    private static decimal? CalculateChangePercentFromPrevious(decimal? previousValue, decimal currentValue)
    {
        if (previousValue is null)
        {
            return null;
        }

        if (previousValue == 0)
        {
            return currentValue == 0 ? 0m : currentValue > 0 ? 100m : -100m;
        }

        return (currentValue - previousValue.Value) / Math.Abs(previousValue.Value) * 100m;
    }

    private static bool TryParseGroupBy(string? value, out ExpenseGroupBy groupBy)
    {
        groupBy = ExpenseGroupBy.Month;

        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "day":
                groupBy = ExpenseGroupBy.Day;
                return true;
            case "week":
                groupBy = ExpenseGroupBy.Week;
                return true;
            case "month":
                groupBy = ExpenseGroupBy.Month;
                return true;
            case "year":
                groupBy = ExpenseGroupBy.Year;
                return true;
            default:
                return false;
        }
    }

    private static List<DateTime> BuildBucketStarts(DateTime rangeStartLocal, DateTime rangeEndExclusiveLocal, ExpenseGroupBy groupBy)
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

    private static DateTime GetBucketStart(DateTime date, ExpenseGroupBy groupBy)
    {
        return groupBy switch
        {
            ExpenseGroupBy.Day => date.Date,
            ExpenseGroupBy.Week => date.Date.AddDays(-GetMondayOffset(date.DayOfWeek)),
            ExpenseGroupBy.Month => new DateTime(date.Year, date.Month, 1),
            ExpenseGroupBy.Year => new DateTime(date.Year, 1, 1),
            _ => date.Date
        };
    }

    private static DateTime GetBucketEndExclusive(DateTime bucketStart, ExpenseGroupBy groupBy)
    {
        return groupBy switch
        {
            ExpenseGroupBy.Day => bucketStart.AddDays(1),
            ExpenseGroupBy.Week => bucketStart.AddDays(7),
            ExpenseGroupBy.Month => bucketStart.AddMonths(1),
            ExpenseGroupBy.Year => bucketStart.AddYears(1),
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

    private static string ToApiKey(ExpenseGroupBy groupBy)
    {
        return groupBy switch
        {
            ExpenseGroupBy.Day => "day",
            ExpenseGroupBy.Week => "week",
            ExpenseGroupBy.Year => "year",
            _ => "month"
        };
    }

    private enum ExpenseGroupBy
    {
        Day,
        Week,
        Month,
        Year
    }

    private sealed class ExpenseRow
    {
        public Ulid? CategoryId { get; set; }
        public string? CategoryName { get; set; }
        public required DateTime LocalDate { get; set; }
        public required decimal Amount { get; set; }
    }

    private sealed class RevenueAppointmentRow
    {
        public required Ulid ServiceId { get; set; }
        public required DateTime StartDate { get; set; }
    }

    private sealed class ServicePriceRow
    {
        public required Ulid ServiceId { get; set; }
        public required DateTime EffectiveDate { get; set; }
        public required decimal Price { get; set; }
    }
}
