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

public class GetClientAnalyticsEndpoint(AppDbContext db, IRecurringAppointmentMaterializer recurringAppointmentMaterializer)
    : Ep.Req<GetClientAnalyticsRequest>.Res<Results<Ok<GetClientAnalyticsResponse>, UnauthorizedHttpResult, ForbidHttpResult, ProblemDetails>>
{
    private const int LostClientWindowDays = 30;
    private const int ActiveClientRecentAppointmentWindowDays = 7;
    private const int RfmScoreBuckets = 5;
    private const int RegularClientWindowDays = 90;
    private const int RegularClientCompletedAppointmentsThreshold = 4;
    private const decimal RiskMultiplier = 1.5m;
    private const string NoSourceLabel = "Без источника";

    public override void Configure()
    {
        Get("/dashboard/clients");
    }

    public override async Task<Results<Ok<GetClientAnalyticsResponse>, UnauthorizedHttpResult, ForbidHttpResult, ProblemDetails>> ExecuteAsync(
        GetClientAnalyticsRequest req,
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
        var rangeEndLocal = req.End.Date;
        var rangeEndExclusiveLocal = rangeEndLocal.AddDays(1);
        var rangeLengthDays = (rangeEndExclusiveLocal - rangeStartLocal).Days;
        var previousRangeStartLocal = rangeStartLocal.AddDays(-rangeLengthDays);
        var previousRangeEndLocal = rangeStartLocal.AddDays(-1);
        var activePaymentWindowStartLocal = rangeEndExclusiveLocal.AddMonths(-1);
        var activeAppointmentWindowStartLocal = rangeEndExclusiveLocal.AddDays(-ActiveClientRecentAppointmentWindowDays);

        var previousRangeStartUtc = TimeZoneInfo.ConvertTimeToUtc(previousRangeStartLocal, timezone);
        var rangeStartUtc = TimeZoneInfo.ConvertTimeToUtc(rangeStartLocal, timezone);
        var rangeEndExclusiveUtc = TimeZoneInfo.ConvertTimeToUtc(rangeEndExclusiveLocal, timezone);
        var regularWindowStartUtc = TimeZoneInfo.ConvertTimeToUtc(rangeEndExclusiveLocal.AddDays(-RegularClientWindowDays), timezone);

        await recurringAppointmentMaterializer.EnsureAppointmentsGeneratedAsync(previousRangeStartUtc, rangeEndExclusiveUtc.AddTicks(-1), ct);

        var clients = await db.Clients
            .AsNoTracking()
            .Select(e => new ClientRow
            {
                ClientId = e.Id,
                ClientDisplayName = $"{e.LastName} {e.FirstName}".Trim(),
                SourceName = e.Source != null ? e.Source.Name : null,
                CreatedAtUtc = e.CreatedAtUtc
            })
            .ToListAsync(ct);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Where(e => !e.IsDeleted && e.StartDate < rangeEndExclusiveUtc)
            .Select(e => new AppointmentRow
            {
                AppointmentId = e.Id,
                ClientId = e.Client.Id,
                ServiceId = e.Service.Id,
                StartDateUtc = e.StartDate,
                Status = e.Status
            })
            .ToListAsync(ct);

        var serviceIds = appointments.Select(e => e.ServiceId).Distinct().ToList();
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

        var paymentRows = await db.Payments
            .AsNoTracking()
            .Where(e => e.Date < rangeEndExclusiveUtc)
            .Select(e => new PaymentRow
            {
                ClientId = e.Client.Id,
                Amount = e.Amount,
                DateUtc = e.Date
            })
            .ToListAsync(ct);

        var paymentsByClient = paymentRows
            .GroupBy(e => e.ClientId)
            .ToDictionary(group => group.Key, group => group.Sum(item => item.Amount));

        var priceLookup = servicePrices
            .GroupBy(e => e.ServiceId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(e => e.EffectiveDate).ToList());

        var previousActiveClientIds = appointments
            .Where(e => e.StartDateUtc >= previousRangeStartUtc && e.StartDateUtc < rangeStartUtc)
            .Select(e => e.ClientId)
            .Distinct()
            .ToHashSet();

        var currentActiveClientIds = appointments
            .Where(e => e.StartDateUtc >= rangeStartUtc && e.StartDateUtc < rangeEndExclusiveUtc)
            .Select(e => e.ClientId)
            .Distinct()
            .ToHashSet();

        var historicalAppointmentsByClient = appointments
            .GroupBy(e => e.ClientId)
            .ToDictionary(group => group.Key, group => group.OrderBy(e => e.StartDateUtc).ThenBy(e => e.AppointmentId).ToList());

        var vipClientIds = BuildVipClientIds(historicalAppointmentsByClient, priceLookup);

        var activeNowClientIds = clients
            .Where(client =>
            {
                var hasRecentPayment = paymentRows.Any(payment =>
                    payment.ClientId == client.ClientId
                    && TimeZoneInfo.ConvertTimeFromUtc(payment.DateUtc, timezone).Date >= activePaymentWindowStartLocal);
                var hasRecentAppointment = historicalAppointmentsByClient.GetValueOrDefault(client.ClientId, []).Any(appointment =>
                    TimeZoneInfo.ConvertTimeFromUtc(appointment.StartDateUtc, timezone).Date >= activeAppointmentWindowStartLocal);

                return hasRecentPayment || hasRecentAppointment;
            })
            .Select(client => client.ClientId)
            .ToHashSet();

        var allAppointmentIntervalsDays = historicalAppointmentsByClient
            .SelectMany(group =>
            {
                var orderedLocalDates = group.Value
                    .Select(e => TimeZoneInfo.ConvertTimeFromUtc(e.StartDateUtc, timezone).Date)
                    .ToList();

                return EnumerateIntervals(orderedLocalDates);
            })
            .ToList();

        var clientDtos = clients
            .Select(client =>
            {
                var clientAppointments = historicalAppointmentsByClient.GetValueOrDefault(client.ClientId, []);
                var appointmentDatesLocal = clientAppointments
                    .Select(e => TimeZoneInfo.ConvertTimeFromUtc(e.StartDateUtc, timezone).Date)
                    .ToList();

                var completedAppointmentsCount = clientAppointments.Count(e => e.Status == AppointmentStatus.Completed);
                var revenueAppointments = clientAppointments
                    .Where(e => e.Status == AppointmentStatus.Completed || e.Status == AppointmentStatus.Burned)
                    .ToList();
                var revenueCountedAppointmentsCount = revenueAppointments.Count;
                var lifetimeValue = revenueAppointments.Sum(e => ResolveAppointmentPrice(e.ServiceId, e.StartDateUtc, priceLookup));
                var firstAppointmentAtUtc = clientAppointments.FirstOrDefault()?.StartDateUtc;
                var lastAppointmentAtUtc = clientAppointments.LastOrDefault()?.StartDateUtc;
                var lifetimeDays = appointmentDatesLocal.Count == 0
                    ? null
                    : (decimal?)(appointmentDatesLocal[^1] - appointmentDatesLocal[0]).TotalDays;
                var averageIntervalDays = CalculateAverageIntervalDays(appointmentDatesLocal);
                DateTime? lastAppointmentLocalDate = lastAppointmentAtUtc is null
                    ? null
                    : TimeZoneInfo.ConvertTimeFromUtc(lastAppointmentAtUtc.Value, timezone).Date;
                var daysSinceLastAppointment = lastAppointmentLocalDate is null
                    ? null
                    : (int?)(rangeEndLocal - lastAppointmentLocalDate.Value).TotalDays;
                var hasHistoricalAppointments = clientAppointments.Count > 0;
                var isNew = client.CreatedAtUtc >= rangeStartUtc && client.CreatedAtUtc < rangeEndExclusiveUtc;
                var isLost = hasHistoricalAppointments
                    && !clientAppointments.Any(e => TimeZoneInfo.ConvertTimeFromUtc(e.StartDateUtc, timezone).Date >= rangeEndExclusiveLocal.AddDays(-LostClientWindowDays));
                var hasAppointmentInPeriod = clientAppointments.Any(e => e.StartDateUtc >= rangeStartUtc && e.StartDateUtc < rangeEndExclusiveUtc);
                var isReturned = clientAppointments.Count >= 2
                    && hasAppointmentInPeriod
                    && firstAppointmentAtUtc is not null
                    && clientAppointments.Any(e => e.StartDateUtc >= rangeStartUtc && e.StartDateUtc < rangeEndExclusiveUtc && e.StartDateUtc > firstAppointmentAtUtc.Value);
                var isAtRisk = !isLost
                    && averageIntervalDays is not null
                    && daysSinceLastAppointment is not null
                    && daysSinceLastAppointment.Value > averageIntervalDays.Value * RiskMultiplier;
                var regularCompletedAppointmentsCount = clientAppointments.Count(e =>
                    e.Status == AppointmentStatus.Completed
                    && e.StartDateUtc >= regularWindowStartUtc
                    && e.StartDateUtc < rangeEndExclusiveUtc);
                var totalPayments = paymentsByClient.GetValueOrDefault(client.ClientId);
                var debt = Math.Max(lifetimeValue - totalPayments, 0m);

                return new ClientAnalyticsDto
                {
                    ClientId = client.ClientId,
                    ClientDisplayName = client.ClientDisplayName,
                    SourceName = string.IsNullOrWhiteSpace(client.SourceName) ? NoSourceLabel : client.SourceName!,
                    LifetimeValue = lifetimeValue,
                    RevenueCountedAppointmentsCount = revenueCountedAppointmentsCount,
                    CompletedAppointmentsCount = completedAppointmentsCount,
                    AverageIntervalDays = averageIntervalDays,
                    LifetimeDays = lifetimeDays,
                    DaysSinceLastAppointment = daysSinceLastAppointment,
                    CreatedAtUtc = client.CreatedAtUtc,
                    FirstAppointmentAtUtc = firstAppointmentAtUtc,
                    LastAppointmentAtUtc = lastAppointmentAtUtc,
                    Debt = debt,
                    IsLost = isLost,
                    IsAtRisk = isAtRisk,
                    IsVip = vipClientIds.Contains(client.ClientId),
                    IsRegular = regularCompletedAppointmentsCount >= RegularClientCompletedAppointmentsThreshold,
                    IsSingleTime = completedAppointmentsCount == 1,
                    IsDebtor = debt > 0m,
                    IsNew = isNew,
                    IsReturned = isReturned
                };
            })
            .OrderByDescending(e => e.LifetimeValue)
            .ThenBy(e => e.ClientDisplayName)
            .ToList();

        var clientsWithAnyAppointmentInPeriodCount = clientDtos.Count(e =>
            e.LastAppointmentAtUtc is not null
            && historicalAppointmentsByClient.GetValueOrDefault(e.ClientId, []).Any(a => a.StartDateUtc >= rangeStartUtc && a.StartDateUtc < rangeEndExclusiveUtc));

        var historicalClients = clientDtos.Where(e => e.LastAppointmentAtUtc is not null).ToList();
        var rfmClients = BuildRfmClients(clientDtos, historicalAppointmentsByClient, priceLookup, timezone, rangeStartUtc, rangeEndExclusiveUtc, rangeEndLocal);

        var sourceDtos = clientDtos
            .GroupBy(e => e.SourceName)
            .Select(group =>
            {
                var groupClientIds = group.Select(e => e.ClientId).ToHashSet();
                var currentPeriodActiveCount = currentActiveClientIds.Count(groupClientIds.Contains);
                var previousActiveCount = previousActiveClientIds.Count(groupClientIds.Contains);
                var retainedCount = currentActiveClientIds.Count(clientId => groupClientIds.Contains(clientId) && previousActiveClientIds.Contains(clientId));
                var newClientsCount = group.Count(e => e.IsNew);
                var lostCount = group.Count(e => e.IsLost);
                var lifetimeValueClients = group.Where(e => e.LifetimeValue > 0m).ToList();
                var historicalGroupClientsCount = group.Count(e => e.LastAppointmentAtUtc is not null);

                return new ClientSourceAnalyticsDto
                {
                    SourceName = group.Key,
                    ClientsCount = group.Count(),
                    ActiveClientsCount = currentPeriodActiveCount,
                    PreviousPeriodActiveClientsCount = previousActiveCount,
                    RetainedClientsCount = retainedCount,
                    RetentionRate = previousActiveCount == 0 ? null : retainedCount / (decimal)previousActiveCount * 100m,
                    NewClientsCount = newClientsCount,
                    NewClientsShare = group.Count() == 0 ? null : newClientsCount / (decimal)group.Count() * 100m,
                    LostClientsCount = lostCount,
                    LostShare = historicalGroupClientsCount == 0 ? null : lostCount / (decimal)historicalGroupClientsCount * 100m,
                    Revenue = group.Sum(e => e.LifetimeValue),
                    AverageLifetimeValue = lifetimeValueClients.Count == 0 ? null : lifetimeValueClients.Average(e => e.LifetimeValue)
                };
            })
            .OrderByDescending(e => e.Revenue)
            .ThenByDescending(e => e.ClientsCount)
            .ThenBy(e => e.SourceName)
            .ToList();

        var retainedClientsCount = previousActiveClientIds.Count(currentActiveClientIds.Contains);
        var lifetimeValueClientsAll = clientDtos.Where(e => e.LifetimeValue > 0m).ToList();
        var returnedClientsCount = clientDtos.Count(e => e.IsReturned);

        return TypedResults.Ok(new GetClientAnalyticsResponse
        {
            StartDate = rangeStartLocal,
            EndDate = rangeEndLocal,
            PreviousPeriodStartDate = previousRangeStartLocal,
            PreviousPeriodEndDate = previousRangeEndLocal,
            TotalClientsCount = clientDtos.Count,
            ActiveNowClientsCount = activeNowClientIds.Count,
            InactiveClientsCount = clientDtos.Count - activeNowClientIds.Count,
            ActiveClientsCount = currentActiveClientIds.Count,
            PreviousPeriodActiveClientsCount = previousActiveClientIds.Count,
            RetainedClientsCount = retainedClientsCount,
            RetentionRate = previousActiveClientIds.Count == 0 ? null : retainedClientsCount / (decimal)previousActiveClientIds.Count * 100m,
            NewClientsCount = clientDtos.Count(e => e.IsNew),
            ReturnedClientsCount = returnedClientsCount,
            ReturningClientsShare = clientsWithAnyAppointmentInPeriodCount == 0 ? null : returnedClientsCount / (decimal)clientsWithAnyAppointmentInPeriodCount * 100m,
            LostClientsCount = clientDtos.Count(e => e.IsLost),
            LostShare = historicalClients.Count == 0 ? null : clientDtos.Count(e => e.IsLost) / (decimal)historicalClients.Count * 100m,
            AtRiskClientsCount = clientDtos.Count(e => e.IsAtRisk),
            AverageIntervalDays = allAppointmentIntervalsDays.Count == 0 ? null : allAppointmentIntervalsDays.Average(),
            AverageLifetimeValue = lifetimeValueClientsAll.Count == 0 ? null : lifetimeValueClientsAll.Average(e => e.LifetimeValue),
            AverageClientLifetimeDays = historicalClients.Count == 0
                ? null
                : historicalClients.Average(e => e.LifetimeDays ?? 0m),
            VipClientsCount = clientDtos.Count(e => e.IsVip),
            RegularClientsCount = clientDtos.Count(e => e.IsRegular),
            SingleTimeClientsCount = clientDtos.Count(e => e.IsSingleTime),
            DebtorsCount = clientDtos.Count(e => e.IsDebtor),
            Sources = sourceDtos,
            Clients = clientDtos,
            RfmClients = rfmClients
        });
    }

    private static List<ClientRfmAnalyticsDto> BuildRfmClients(
        IReadOnlyList<ClientAnalyticsDto> clientDtos,
        IReadOnlyDictionary<Ulid, List<AppointmentRow>> historicalAppointmentsByClient,
        IReadOnlyDictionary<Ulid, List<ServicePriceRow>> priceLookup,
        TimeZoneInfo timezone,
        DateTime rangeStartUtc,
        DateTime rangeEndExclusiveUtc,
        DateTime rangeEndLocal)
    {
        var rows = clientDtos
            .Select(client =>
            {
                var appointments = historicalAppointmentsByClient.GetValueOrDefault(client.ClientId, []);
                var completedAppointmentsInPeriod = appointments.Count(e =>
                    e.Status == AppointmentStatus.Completed
                    && e.StartDateUtc >= rangeStartUtc
                    && e.StartDateUtc < rangeEndExclusiveUtc);
                var revenueAppointmentsInPeriod = appointments.Where(e =>
                    (e.Status == AppointmentStatus.Completed || e.Status == AppointmentStatus.Burned)
                    && e.StartDateUtc >= rangeStartUtc
                    && e.StartDateUtc < rangeEndExclusiveUtc);
                var monetary = revenueAppointmentsInPeriod.Sum(e => ResolveAppointmentPrice(e.ServiceId, e.StartDateUtc, priceLookup));
                var recencyDays = client.LastAppointmentAtUtc is null
                    ? (int?)null
                    : (int?)(rangeEndLocal - TimeZoneInfo.ConvertTimeFromUtc(client.LastAppointmentAtUtc.Value, timezone).Date).TotalDays;

                return new ClientRfmRow
                {
                    ClientId = client.ClientId,
                    ClientDisplayName = client.ClientDisplayName,
                    SourceName = client.SourceName,
                    RecencyDays = recencyDays,
                    Frequency = completedAppointmentsInPeriod,
                    Monetary = monetary
                };
            })
            .ToList();

        var recencyScores = BuildBucketScores(rows.Where(e => e.RecencyDays is not null).OrderBy(e => e.RecencyDays).Select(e => e.ClientId).ToList(), reverse: true);
        var frequencyScores = BuildBucketScores(rows.OrderByDescending(e => e.Frequency).ThenBy(e => e.ClientId).Select(e => e.ClientId).ToList(), reverse: true);
        var monetaryScores = BuildBucketScores(rows.OrderByDescending(e => e.Monetary).ThenBy(e => e.ClientId).Select(e => e.ClientId).ToList(), reverse: true);

        return rows
            .Select(row =>
            {
                var recencyScore = row.RecencyDays is null ? 1 : recencyScores.GetValueOrDefault(row.ClientId, 1);
                var frequencyScore = row.Frequency == 0 ? 1 : frequencyScores.GetValueOrDefault(row.ClientId, 1);
                var monetaryScore = row.Monetary == 0m ? 1 : monetaryScores.GetValueOrDefault(row.ClientId, 1);

                return new ClientRfmAnalyticsDto
                {
                    ClientId = row.ClientId,
                    ClientDisplayName = row.ClientDisplayName,
                    SourceName = row.SourceName,
                    RecencyDays = row.RecencyDays,
                    Frequency = row.Frequency,
                    Monetary = row.Monetary,
                    RecencyScore = recencyScore,
                    FrequencyScore = frequencyScore,
                    MonetaryScore = monetaryScore,
                    RfmScore = $"{recencyScore}{frequencyScore}{monetaryScore}",
                    Segment = ResolveRfmSegment(recencyScore, frequencyScore, monetaryScore, row.RecencyDays)
                };
            })
            .OrderByDescending(e => e.RecencyScore + e.FrequencyScore + e.MonetaryScore)
            .ThenByDescending(e => e.Monetary)
            .ThenBy(e => e.ClientDisplayName)
            .ToList();
    }

    private static HashSet<Ulid> BuildVipClientIds(
        IReadOnlyDictionary<Ulid, List<AppointmentRow>> historicalAppointmentsByClient,
        IReadOnlyDictionary<Ulid, List<ServicePriceRow>> priceLookup)
    {
        var clientLifetimeValues = historicalAppointmentsByClient
            .Select(group => new
            {
                ClientId = group.Key,
                LifetimeValue = group.Value
                    .Where(e => e.Status == AppointmentStatus.Completed || e.Status == AppointmentStatus.Burned)
                    .Sum(e => ResolveAppointmentPrice(e.ServiceId, e.StartDateUtc, priceLookup))
            })
            .Where(e => e.LifetimeValue > 0m)
            .OrderByDescending(e => e.LifetimeValue)
            .ThenBy(e => e.ClientId)
            .ToList();

        if (clientLifetimeValues.Count == 0)
        {
            return [];
        }

        var vipCount = Math.Max(1, (int)Math.Ceiling(clientLifetimeValues.Count * 0.1m));
        return clientLifetimeValues
            .Take(vipCount)
            .Select(e => e.ClientId)
            .ToHashSet();
    }

    private static decimal? CalculateAverageIntervalDays(IReadOnlyList<DateTime> appointmentDatesLocal)
    {
        if (appointmentDatesLocal.Count < 2)
        {
            return null;
        }

        var intervals = new List<decimal>();
        for (var i = 1; i < appointmentDatesLocal.Count; i++)
        {
            intervals.Add((decimal)(appointmentDatesLocal[i] - appointmentDatesLocal[i - 1]).TotalDays);
        }

        return intervals.Count == 0 ? null : intervals.Average();
    }

    private static IEnumerable<decimal> EnumerateIntervals(IReadOnlyList<DateTime> appointmentDatesLocal)
    {
        if (appointmentDatesLocal.Count < 2)
        {
            yield break;
        }

        for (var i = 1; i < appointmentDatesLocal.Count; i++)
        {
            yield return (decimal)(appointmentDatesLocal[i] - appointmentDatesLocal[i - 1]).TotalDays;
        }
    }

    private static Dictionary<Ulid, int> BuildBucketScores(IReadOnlyList<Ulid> orderedClientIds, bool reverse)
    {
        if (orderedClientIds.Count == 0)
        {
            return [];
        }

        var scores = new Dictionary<Ulid, int>(orderedClientIds.Count);
        for (var i = 0; i < orderedClientIds.Count; i++)
        {
            var bucket = (int)Math.Floor(i * RfmScoreBuckets / (decimal)orderedClientIds.Count) + 1;
            var normalizedBucket = Math.Clamp(bucket, 1, RfmScoreBuckets);
            scores[orderedClientIds[i]] = reverse ? RfmScoreBuckets - normalizedBucket + 1 : normalizedBucket;
        }

        return scores;
    }

    private static string ResolveRfmSegment(int recencyScore, int frequencyScore, int monetaryScore, int? recencyDays)
    {
        if (recencyDays is null)
        {
            return "Низкая ценность";
        }

        if (recencyScore >= 4 && frequencyScore >= 4 && monetaryScore >= 4)
        {
            return "Лучшие";
        }

        if (frequencyScore >= 4 && monetaryScore >= 3)
        {
            return "Лояльные";
        }

        if (recencyScore >= 4 && (frequencyScore >= 2 || monetaryScore >= 2))
        {
            return "Перспективные";
        }

        if (recencyScore <= 2 && (frequencyScore >= 3 || monetaryScore >= 3))
        {
            return "Под риском";
        }

        if (recencyScore == 1 && frequencyScore <= 2 && monetaryScore <= 2)
        {
            return "Потерянные";
        }

        return "Низкая ценность";
    }

    private static decimal ResolveAppointmentPrice(
        Ulid serviceId,
        DateTime appointmentStartDateUtc,
        IReadOnlyDictionary<Ulid, List<ServicePriceRow>> priceLookup)
    {
        if (!priceLookup.TryGetValue(serviceId, out var prices))
        {
            return 0m;
        }

        return prices
            .Where(price => price.EffectiveDate <= appointmentStartDateUtc)
            .Select(price => price.Price)
            .FirstOrDefault();
    }

    private sealed class ClientRow
    {
        public required Ulid ClientId { get; set; }
        public required string ClientDisplayName { get; set; }
        public string? SourceName { get; set; }
        public required DateTime CreatedAtUtc { get; set; }
    }

    private sealed class AppointmentRow
    {
        public required Ulid AppointmentId { get; set; }
        public required Ulid ClientId { get; set; }
        public required Ulid ServiceId { get; set; }
        public required DateTime StartDateUtc { get; set; }
        public required AppointmentStatus Status { get; set; }
    }

    private sealed class ServicePriceRow
    {
        public required Ulid ServiceId { get; set; }
        public required DateTime EffectiveDate { get; set; }
        public required decimal Price { get; set; }
    }

    private sealed class PaymentRow
    {
        public required Ulid ClientId { get; set; }
        public required decimal Amount { get; set; }
        public required DateTime DateUtc { get; set; }
    }

    private sealed class ClientRfmRow
    {
        public required Ulid ClientId { get; set; }
        public required string ClientDisplayName { get; set; }
        public required string SourceName { get; set; }
        public int? RecencyDays { get; set; }
        public required int Frequency { get; set; }
        public required decimal Monetary { get; set; }
    }
}
