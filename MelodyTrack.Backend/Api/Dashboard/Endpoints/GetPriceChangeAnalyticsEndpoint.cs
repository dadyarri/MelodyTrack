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

public class GetPriceChangeAnalyticsEndpoint(AppDbContext db, IRecurringAppointmentMaterializer recurringAppointmentMaterializer)
    : Ep.Req<GetPriceChangeAnalyticsRequest>.Res<Results<Ok<GetPriceChangeAnalyticsResponse>, UnauthorizedHttpResult, ForbidHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/dashboard/price-changes");
    }

    public override async Task<Results<Ok<GetPriceChangeAnalyticsResponse>, UnauthorizedHttpResult, ForbidHttpResult, ProblemDetails>> ExecuteAsync(
        GetPriceChangeAnalyticsRequest req,
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

        if (req.WindowDays <= 0)
        {
            AddError(r => r.WindowDays, "Окно сравнения должно быть больше нуля.");
            return new ProblemDetails(ValidationFailures);
        }

        var rangeStartLocal = req.Start.Date;
        var rangeEndExclusiveLocal = req.End.Date.AddDays(1);
        var rangeStartUtc = TimeZoneInfo.ConvertTimeToUtc(rangeStartLocal, timezone);
        var rangeEndExclusiveUtc = TimeZoneInfo.ConvertTimeToUtc(rangeEndExclusiveLocal, timezone);

        var selectedRows = await db.ServicePriceHistory
            .AsNoTracking()
            .Where(e => e.EffectiveDate >= rangeStartUtc && e.EffectiveDate < rangeEndExclusiveUtc)
            .Select(e => new PriceChangeRow
            {
                PriceId = e.Id,
                ServiceId = e.Service.Id,
                ServiceName = e.Service.Name,
                EffectiveDate = e.EffectiveDate,
                Price = e.Price
            })
            .OrderBy(e => e.ServiceName)
            .ThenBy(e => e.EffectiveDate)
            .ToListAsync(ct);

        if (selectedRows.Count == 0)
        {
            return TypedResults.Ok(CreateEmptyResponse(rangeStartLocal, req.End.Date, req.WindowDays));
        }

        var changedServiceIds = selectedRows.Select(e => e.ServiceId).Distinct().ToList();
        var servicePriceRows = await db.ServicePriceHistory
            .AsNoTracking()
            .Where(e => changedServiceIds.Contains(e.Service.Id) && e.EffectiveDate < rangeEndExclusiveUtc)
            .Select(e => new PriceChangeRow
            {
                PriceId = e.Id,
                ServiceId = e.Service.Id,
                ServiceName = e.Service.Name,
                EffectiveDate = e.EffectiveDate,
                Price = e.Price
            })
            .OrderBy(e => e.ServiceId)
            .ThenBy(e => e.EffectiveDate)
            .ToListAsync(ct);

        var priceLookup = servicePriceRows
            .GroupBy(e => e.ServiceId)
            .ToDictionary(group => group.Key, group => group.OrderBy(e => e.EffectiveDate).ToList());

        var changes = selectedRows
            .Select(row =>
            {
                var serviceHistory = priceLookup[row.ServiceId];
                var currentIndex = serviceHistory.FindIndex(item => item.PriceId == row.PriceId);
                if (currentIndex <= 0)
                {
                    return null;
                }

                var previous = serviceHistory[currentIndex - 1];
                if (previous.Price == row.Price)
                {
                    return null;
                }

                return new PriceChangeEvent
                {
                    ServiceId = row.ServiceId,
                    ServiceName = row.ServiceName,
                    EffectiveDate = row.EffectiveDate,
                    OldPrice = previous.Price,
                    NewPrice = row.Price
                };
            })
            .Where(e => e is not null)
            .Cast<PriceChangeEvent>()
            .OrderByDescending(e => e.EffectiveDate)
            .ThenBy(e => e.ServiceName)
            .ToList();

        if (changes.Count == 0)
        {
            return TypedResults.Ok(CreateEmptyResponse(rangeStartLocal, req.End.Date, req.WindowDays));
        }

        var comparisonStartUtc = changes.Min(e => e.EffectiveDate.AddDays(-req.WindowDays));
        var comparisonEndUtc = changes.Max(e => e.EffectiveDate.AddDays(req.WindowDays));

        await recurringAppointmentMaterializer.EnsureAppointmentsGeneratedAsync(comparisonStartUtc, comparisonEndUtc.AddTicks(-1), ct);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Where(e => !e.IsDeleted
                        && changedServiceIds.Contains(e.Service.Id)
                        && e.StartDate >= comparisonStartUtc
                        && e.StartDate < comparisonEndUtc)
            .Select(e => new AppointmentRow
            {
                ServiceId = e.Service.Id,
                ClientId = e.Client.Id,
                ClientFirstName = e.Client.FirstName,
                ClientLastName = e.Client.LastName,
                ClientSourceName = e.Client.Source != null ? e.Client.Source.Name : null,
                StartDate = e.StartDate,
                Status = e.Status,
                ProviderId = e.Provider != null ? e.Provider.Id : null,
                ProviderFirstName = e.Provider != null ? e.Provider.FirstName : null,
                ProviderLastName = e.Provider != null ? e.Provider.LastName : null
            })
            .ToListAsync(ct);

        var expenses = await db.Expenses
            .AsNoTracking()
            .Where(e => e.Date >= comparisonStartUtc && e.Date < comparisonEndUtc)
            .Select(e => new ExpenseRow
            {
                Date = e.Date,
                Amount = e.Amount
            })
            .ToListAsync(ct);

        var changeDtos = changes
            .Select(change => BuildChangeDto(change, appointments, expenses, priceLookup, req.WindowDays))
            .ToList();

        var strongestPositiveImpacts = changeDtos
            .Where(e => e.PriceChange > 0)
            .OrderByDescending(e => e.RevenueChange)
            .ThenByDescending(e => e.RevenueChangePercent ?? decimal.MinValue)
            .ThenByDescending(e => e.ProfitImpact)
            .ThenByDescending(e => e.AdditionalRevenue ?? decimal.MinValue)
            .Take(10)
            .Select(MapRanking)
            .ToList();

        var negativeImpacts = changeDtos
            .Where(e => e.PriceChange > 0
                        && (e.RevenueAfter < e.RevenueBefore
                            || e.AppointmentsAfter < e.AppointmentsBefore
                            || (e.CancellationShareAfter ?? 0) > (e.CancellationShareBefore ?? 0)
                            || (e.BurnedShareAfter ?? 0) > (e.BurnedShareBefore ?? 0)
                            || e.StoppedClientsCount > 0))
            .OrderBy(e => e.RevenueChange)
            .ThenBy(e => e.AppointmentChange)
            .ThenByDescending(e => e.ChurnShare ?? decimal.MinValue)
            .Take(10)
            .Select(MapRanking)
            .ToList();

        return TypedResults.Ok(new GetPriceChangeAnalyticsResponse
        {
            StartDate = rangeStartLocal,
            EndDate = req.End.Date,
            WindowDays = req.WindowDays,
            TotalChanges = changeDtos.Count,
            PriceIncreasesCount = changeDtos.Count(e => e.PriceChange > 0),
            PriceDecreasesCount = changeDtos.Count(e => e.PriceChange < 0),
            PositiveRevenueImpactCount = changeDtos.Count(e => e.RevenueChange > 0),
            NegativeDemandImpactCount = changeDtos.Count(e => e.AppointmentChange < 0),
            Changes = changeDtos,
            StrongestPositiveImpacts = strongestPositiveImpacts,
            NegativeImpacts = negativeImpacts
        });
    }

    private static GetPriceChangeAnalyticsResponse CreateEmptyResponse(DateTime startDate, DateTime endDate, int windowDays)
    {
        return new GetPriceChangeAnalyticsResponse
        {
            StartDate = startDate,
            EndDate = endDate,
            WindowDays = windowDays,
            TotalChanges = 0,
            PriceIncreasesCount = 0,
            PriceDecreasesCount = 0,
            PositiveRevenueImpactCount = 0,
            NegativeDemandImpactCount = 0,
            Changes = [],
            StrongestPositiveImpacts = [],
            NegativeImpacts = []
        };
    }

    private static PriceChangeAnalyticsDto BuildChangeDto(
        PriceChangeEvent change,
        IReadOnlyCollection<AppointmentRow> appointments,
        IReadOnlyCollection<ExpenseRow> expenses,
        IReadOnlyDictionary<Ulid, List<PriceChangeRow>> priceLookup,
        int windowDays)
    {
        var beforeStart = change.EffectiveDate.AddDays(-windowDays);
        var beforeEnd = change.EffectiveDate;
        var afterStart = change.EffectiveDate;
        var afterEnd = change.EffectiveDate.AddDays(windowDays);

        var serviceAppointments = appointments
            .Where(e => e.ServiceId == change.ServiceId)
            .ToList();

        var beforeAppointments = serviceAppointments
            .Where(e => e.StartDate >= beforeStart && e.StartDate < beforeEnd)
            .ToList();

        var afterAppointments = serviceAppointments
            .Where(e => e.StartDate >= afterStart && e.StartDate < afterEnd)
            .ToList();

        var beforeRevenueAppointments = beforeAppointments
            .Where(e => e.Status.CountsAsRevenue())
            .ToList();

        var afterRevenueAppointments = afterAppointments
            .Where(e => e.Status.CountsAsRevenue())
            .ToList();

        var beforeRevenue = beforeRevenueAppointments.Sum(appointment => ResolveAppointmentPrice(change.ServiceId, appointment.StartDate, priceLookup));
        var afterRevenue = afterRevenueAppointments.Sum(appointment => ResolveAppointmentPrice(change.ServiceId, appointment.StartDate, priceLookup));
        var beforeExpenses = expenses
            .Where(e => e.Date >= beforeStart && e.Date < beforeEnd)
            .Sum(e => e.Amount);
        var afterExpenses = expenses
            .Where(e => e.Date >= afterStart && e.Date < afterEnd)
            .Sum(e => e.Amount);

        var teachers = serviceAppointments
            .GroupBy(e => new
            {
                e.ProviderId,
                ProviderDisplayName = e.ProviderId is null
                    ? "Без преподавателя"
                    : $"{e.ProviderLastName} {e.ProviderFirstName}".Trim()
            })
            .Select(group =>
            {
                var teacherBefore = group
                    .Where(e => e.StartDate >= beforeStart && e.StartDate < beforeEnd)
                    .ToList();
                var teacherAfter = group
                    .Where(e => e.StartDate >= afterStart && e.StartDate < afterEnd)
                    .ToList();
                var teacherBeforeRevenueAppointments = teacherBefore.Where(e => e.Status.CountsAsRevenue()).ToList();
                var teacherAfterRevenueAppointments = teacherAfter.Where(e => e.Status.CountsAsRevenue()).ToList();
                var teacherBeforeRevenue = teacherBeforeRevenueAppointments.Sum(appointment => ResolveAppointmentPrice(change.ServiceId, appointment.StartDate, priceLookup));
                var teacherAfterRevenue = teacherAfterRevenueAppointments.Sum(appointment => ResolveAppointmentPrice(change.ServiceId, appointment.StartDate, priceLookup));

                return new PriceChangeTeacherImpactDto
                {
                    TeacherId = group.Key.ProviderId,
                    TeacherDisplayName = group.Key.ProviderDisplayName,
                    RevenueBefore = teacherBeforeRevenue,
                    RevenueAfter = teacherAfterRevenue,
                    AppointmentsBefore = teacherBefore.Count,
                    AppointmentsAfter = teacherAfter.Count,
                    AverageReceiptBefore = CalculateAverageReceipt(teacherBeforeRevenueAppointments.Count, teacherBeforeRevenue),
                    AverageReceiptAfter = CalculateAverageReceipt(teacherAfterRevenueAppointments.Count, teacherAfterRevenue),
                    CancellationShareBefore = CalculateStatusShare(teacherBefore, AppointmentStatus.Cancelled),
                    CancellationShareAfter = CalculateStatusShare(teacherAfter, AppointmentStatus.Cancelled),
                    BurnedShareBefore = CalculateStatusShare(teacherBefore, AppointmentStatus.Burned),
                    BurnedShareAfter = CalculateStatusShare(teacherAfter, AppointmentStatus.Burned)
                };
            })
            .Where(e => e.AppointmentsBefore > 0 || e.AppointmentsAfter > 0)
            .OrderByDescending(e => e.RevenueAfter - e.RevenueBefore)
            .ThenBy(e => e.TeacherDisplayName)
            .ToList();

        var clients = serviceAppointments
            .GroupBy(e => new
            {
                e.ClientId,
                ClientDisplayName = $"{e.ClientLastName} {e.ClientFirstName}".Trim(),
                e.ClientSourceName
            })
            .Select(group =>
            {
                var clientBefore = group
                    .Where(e => e.StartDate >= beforeStart && e.StartDate < beforeEnd)
                    .OrderBy(e => e.StartDate)
                    .ToList();
                var clientAfter = group
                    .Where(e => e.StartDate >= afterStart && e.StartDate < afterEnd)
                    .OrderBy(e => e.StartDate)
                    .ToList();
                var clientBeforeRevenue = clientBefore
                    .Where(e => e.Status.CountsAsRevenue())
                    .Sum(appointment => ResolveAppointmentPrice(change.ServiceId, appointment.StartDate, priceLookup));
                var clientAfterRevenue = clientAfter
                    .Where(e => e.Status.CountsAsRevenue())
                    .Sum(appointment => ResolveAppointmentPrice(change.ServiceId, appointment.StartDate, priceLookup));
                var continued = clientBefore.Count > 0 && clientAfter.Count > 0;
                var stopped = clientBefore.Count > 0 && clientAfter.Count == 0;
                var reducedFrequency = clientBefore.Count > 0 && clientAfter.Count > 0 && clientAfter.Count < clientBefore.Count;
                var increasedFrequency = clientBefore.Count > 0 && clientAfter.Count > 0 && clientAfter.Count > clientBefore.Count;

                return new PriceChangeClientImpactDto
                {
                    ClientId = group.Key.ClientId,
                    ClientDisplayName = group.Key.ClientDisplayName,
                    SourceName = group.Key.ClientSourceName,
                    AppointmentsBefore = clientBefore.Count,
                    AppointmentsAfter = clientAfter.Count,
                    RevenueBefore = clientBeforeRevenue,
                    RevenueAfter = clientAfterRevenue,
                    AverageIntervalBeforeDays = CalculateAverageIntervalDays(clientBefore.Select(e => e.StartDate).ToList()),
                    AverageIntervalAfterDays = CalculateAverageIntervalDays(clientAfter.Select(e => e.StartDate).ToList()),
                    ContinuedAfterPriceIncrease = continued,
                    StoppedAfterPriceIncrease = stopped,
                    ReducedAppointmentFrequency = reducedFrequency,
                    IncreasedAppointmentFrequency = increasedFrequency
                };
            })
            .Where(e => e.AppointmentsBefore > 0 || e.AppointmentsAfter > 0)
            .OrderByDescending(e => e.RevenueAfter - e.RevenueBefore)
            .ThenBy(e => e.ClientDisplayName)
            .ToList();

        var activeClientsBeforeCount = clients.Count(e => e.AppointmentsBefore > 0);
        var continuedClientsCount = clients.Count(e => e.ContinuedAfterPriceIncrease);
        var stoppedClientsCount = clients.Count(e => e.StoppedAfterPriceIncrease);
        var reducedFrequencyClientsCount = clients.Count(e => e.ReducedAppointmentFrequency);
        var increasedFrequencyClientsCount = clients.Count(e => e.IncreasedAppointmentFrequency);

        var priceChange = change.NewPrice - change.OldPrice;
        var appointmentChange = afterAppointments.Count - beforeAppointments.Count;
        var revenueChange = afterRevenue - beforeRevenue;
        var quantityChangePercent = CalculatePercentChange(beforeAppointments.Count, appointmentChange);
        var priceChangePercent = change.OldPrice == 0 ? (decimal?)null : priceChange / change.OldPrice * 100m;
        var rawPriceChangeRatio = change.OldPrice == 0 ? (decimal?)null : priceChange / change.OldPrice;
        var rawQuantityChangeRatio = beforeAppointments.Count == 0 ? (decimal?)null : (decimal)appointmentChange / beforeAppointments.Count;

        return new PriceChangeAnalyticsDto
        {
            ServiceId = change.ServiceId,
            ServiceName = change.ServiceName,
            EffectiveDate = change.EffectiveDate,
            OldPrice = change.OldPrice,
            NewPrice = change.NewPrice,
            PriceChange = priceChange,
            PriceChangePercent = priceChangePercent,
            AffectedAppointmentsCount = afterAppointments.Count,
            RevenueBefore = beforeRevenue,
            RevenueAfter = afterRevenue,
            RevenueChange = revenueChange,
            RevenueChangePercent = CalculatePercentChange(beforeRevenue, revenueChange),
            AppointmentsBefore = beforeAppointments.Count,
            AppointmentsAfter = afterAppointments.Count,
            AppointmentChange = appointmentChange,
            AppointmentChangePercent = quantityChangePercent,
            CompletedAppointmentsBefore = beforeAppointments.Count(e => e.Status == AppointmentStatus.Completed),
            CompletedAppointmentsAfter = afterAppointments.Count(e => e.Status == AppointmentStatus.Completed),
            CancellationShareBefore = CalculateStatusShare(beforeAppointments, AppointmentStatus.Cancelled),
            CancellationShareAfter = CalculateStatusShare(afterAppointments, AppointmentStatus.Cancelled),
            BurnedShareBefore = CalculateStatusShare(beforeAppointments, AppointmentStatus.Burned),
            BurnedShareAfter = CalculateStatusShare(afterAppointments, AppointmentStatus.Burned),
            AverageReceiptBefore = CalculateAverageReceipt(beforeRevenueAppointments.Count, beforeRevenue),
            AverageReceiptAfter = CalculateAverageReceipt(afterRevenueAppointments.Count, afterRevenue),
            ExpensesBefore = beforeExpenses,
            ExpensesAfter = afterExpenses,
            NetProfitBefore = beforeRevenue - beforeExpenses,
            NetProfitAfter = afterRevenue - afterExpenses,
            ProfitImpact = (afterRevenue - afterExpenses) - (beforeRevenue - beforeExpenses),
            PriceElasticity = rawQuantityChangeRatio is null || rawPriceChangeRatio is null || rawPriceChangeRatio == 0
                ? null
                : rawQuantityChangeRatio / rawPriceChangeRatio,
            AdditionalRevenue = afterRevenueAppointments.Count == 0
                ? 0m
                : afterRevenue - afterRevenueAppointments.Count * change.OldPrice,
            ActiveClientsBeforeCount = activeClientsBeforeCount,
            ContinuedClientsCount = continuedClientsCount,
            StoppedClientsCount = stoppedClientsCount,
            ReducedFrequencyClientsCount = reducedFrequencyClientsCount,
            IncreasedFrequencyClientsCount = increasedFrequencyClientsCount,
            ChurnShare = activeClientsBeforeCount == 0 ? null : stoppedClientsCount / (decimal)activeClientsBeforeCount * 100m,
            Teachers = teachers,
            Clients = clients
        };
    }

    private static PriceChangeRankingDto MapRanking(PriceChangeAnalyticsDto change)
    {
        return new PriceChangeRankingDto
        {
            ServiceId = change.ServiceId,
            ServiceName = change.ServiceName,
            EffectiveDate = change.EffectiveDate,
            RevenueChange = change.RevenueChange,
            RevenueChangePercent = change.RevenueChangePercent,
            ProfitImpact = change.ProfitImpact,
            AppointmentChange = change.AppointmentChange,
            AppointmentChangePercent = change.AppointmentChangePercent,
            AdditionalRevenue = change.AdditionalRevenue,
            ChurnShare = change.ChurnShare,
            CancellationShareBefore = change.CancellationShareBefore,
            CancellationShareAfter = change.CancellationShareAfter,
            BurnedShareBefore = change.BurnedShareBefore,
            BurnedShareAfter = change.BurnedShareAfter
        };
    }

    private static decimal? CalculateAverageReceipt(int appointmentCount, decimal revenue)
    {
        return appointmentCount == 0 ? null : revenue / appointmentCount;
    }

    private static decimal? CalculateAverageIntervalDays(IReadOnlyList<DateTime> appointments)
    {
        if (appointments.Count < 2)
        {
            return null;
        }

        var intervals = new List<decimal>();
        for (var i = 1; i < appointments.Count; i++)
        {
            intervals.Add((decimal)(appointments[i] - appointments[i - 1]).TotalDays);
        }

        return intervals.Average();
    }

    private static decimal? CalculatePercentChange(decimal beforeValue, decimal change)
    {
        return beforeValue == 0 ? null : change / beforeValue * 100m;
    }

    private static decimal? CalculateStatusShare(IReadOnlyCollection<AppointmentRow> appointments, AppointmentStatus status)
    {
        return appointments.Count == 0
            ? null
            : appointments.Count(e => e.Status == status) / (decimal)appointments.Count * 100m;
    }

    private static decimal ResolveAppointmentPrice(
        Ulid serviceId,
        DateTime appointmentStartDate,
        IReadOnlyDictionary<Ulid, List<PriceChangeRow>> priceLookup)
    {
        if (!priceLookup.TryGetValue(serviceId, out var prices))
        {
            return 0m;
        }

        return prices
            .Where(price => price.EffectiveDate <= appointmentStartDate)
            .OrderByDescending(price => price.EffectiveDate)
            .Select(price => price.Price)
            .FirstOrDefault();
    }

    private sealed class PriceChangeRow
    {
        public required Ulid PriceId { get; set; }
        public required Ulid ServiceId { get; set; }
        public required string ServiceName { get; set; }
        public required DateTime EffectiveDate { get; set; }
        public required decimal Price { get; set; }
    }

    private sealed class PriceChangeEvent
    {
        public required Ulid ServiceId { get; set; }
        public required string ServiceName { get; set; }
        public required DateTime EffectiveDate { get; set; }
        public required decimal OldPrice { get; set; }
        public required decimal NewPrice { get; set; }
    }

    private sealed class AppointmentRow
    {
        public required Ulid ServiceId { get; set; }
        public required Ulid ClientId { get; set; }
        public required string ClientFirstName { get; set; }
        public required string ClientLastName { get; set; }
        public string? ClientSourceName { get; set; }
        public required DateTime StartDate { get; set; }
        public required AppointmentStatus Status { get; set; }
        public Ulid? ProviderId { get; set; }
        public string? ProviderFirstName { get; set; }
        public string? ProviderLastName { get; set; }
    }

    private sealed class ExpenseRow
    {
        public required DateTime Date { get; set; }
        public required decimal Amount { get; set; }
    }
}
