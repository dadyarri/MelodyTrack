using MelodyTrack.Backend.Api.Reports.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Reports.Reporting;

public interface IClientsReportQueryService
{
    Task<ClientsReportResponse> GetAsync(ReportContext context, CancellationToken ct);
}

public sealed class ClientsReportQueryService(
    AppDbContext db,
    IReportContextFactory contextFactory,
    IReportAppointmentQuery appointmentQuery,
    IRecurringAppointmentMaterializer materializer) : IClientsReportQueryService
{
    private const int AtRiskAfterDays = 30;
    private const int LostAfterDays = 60;

    public async Task<ClientsReportResponse> GetAsync(ReportContext context, CancellationToken ct)
    {
        var periodLength = (context.EndExclusiveLocal - context.StartLocal).Days;
        var previousStartLocal = context.StartLocal.AddDays(-periodLength);
        var previousStartUtc = ReportBuckets.ToUtc(previousStartLocal, context.Timezone);
        await materializer.EnsureAppointmentsGeneratedAsync(previousStartUtc, context.EndExclusiveUtc.AddTicks(-1), ct);

        var appointments = await appointmentQuery.LoadAsync(context, DateTime.UnixEpoch, context.EndExclusiveUtc, ct);
        var visits = appointments.Where(appointment => appointment.IsValueVisit).ToList();
        var currentVisits = visits
            .Where(appointment => appointment.StartLocal >= context.StartLocal && appointment.StartLocal < context.EndExclusiveLocal)
            .ToList();
        var previousVisits = visits
            .Where(appointment => appointment.StartLocal >= previousStartLocal && appointment.StartLocal < context.StartLocal)
            .ToList();
        var currentClientIds = currentVisits.Select(appointment => appointment.ClientId).ToHashSet();
        var previousClientIds = previousVisits.Select(appointment => appointment.ClientId).ToHashSet();
        var retainedClientIds = previousClientIds.Where(currentClientIds.Contains).ToHashSet();
        var clientIds = visits.Select(appointment => appointment.ClientId).Distinct().ToList();
        var reportEndDate = DateOnly.FromDateTime(context.EndLocal);
        var vacations = clientIds.Count == 0
            ? []
            : await db.ClientVacations.AsNoTracking()
                .Where(vacation => clientIds.Contains(vacation.ClientId)
                                   && vacation.StartDate <= reportEndDate)
                .Select(vacation => new ClientVacationRow(vacation.ClientId, vacation.StartDate, vacation.EndDate))
                .ToListAsync(ct);
        var vacationsByClient = vacations
            .GroupBy(vacation => vacation.ClientId)
            .ToDictionary(group => group.Key, group => (IReadOnlyCollection<ClientVacationRow>)group.ToList());
        var clientRows = BuildClientRows(visits, currentClientIds, vacationsByClient, context);
        var acquiredClientIds = clientRows
            .Where(client => client.FirstVisitLocal >= context.StartLocal && client.FirstVisitLocal < context.EndExclusiveLocal)
            .Select(client => client.ClientId)
            .ToHashSet();
        var valuedClients = clientRows.Where(client => client.Value > 0m).ToList();

        return new ClientsReportResponse
        {
            Context = await contextFactory.CreateDtoAsync(context, ct),
            Summary = new ClientsReportSummaryDto
            {
                AcquiredClients = acquiredClientIds.Count,
                ActiveClients = currentClientIds.Count,
                RetainedClients = retainedClientIds.Count,
                RetentionPercent = ReportBuckets.Percent(retainedClientIds.Count, previousClientIds.Count),
                AtRiskClients = clientRows.Count(client => client.ActivityState == "at-risk"),
                LostClients = clientRows.Count(client => client.ActivityState == "lost"),
                OnVacationClients = clientRows.Count(client => client.ActivityState == "on-vacation"),
                AverageVisitFrequency = currentClientIds.Count == 0 ? null : currentVisits.Count / (decimal)currentClientIds.Count,
                AverageClientValue = valuedClients.Count == 0 ? null : valuedClients.Average(client => client.Value)
            },
            Trend = BuildTrend(context, visits, clientRows),
            Sources = clientRows
                .GroupBy(client => client.SourceName)
                .Select(group => new ClientSourceReportDto
                {
                    SourceName = group.Key,
                    AcquiredClients = group.Count(client => acquiredClientIds.Contains(client.ClientId)),
                    ActiveClients = group.Count(client => currentClientIds.Contains(client.ClientId)),
                    ClientValue = group.Sum(client => client.Value)
                })
                .OrderByDescending(item => item.AcquiredClients)
                .ThenByDescending(item => item.ClientValue)
                .ThenBy(item => item.SourceName)
                .ToList(),
            Clients = clientRows
                .OrderByDescending(client => currentClientIds.Contains(client.ClientId))
                .ThenByDescending(client => client.Value)
                .ThenBy(client => client.ClientName)
                .Take(100)
                .Select(client => new ClientValueReportDto
                {
                    ClientId = client.ClientId,
                    ClientName = client.ClientName,
                    SourceName = client.SourceName,
                    Visits = client.Visits,
                    Value = client.Value,
                    AverageIntervalDays = client.AverageIntervalDays,
                    LastVisitAtUtc = client.LastVisitAtUtc,
                    ActivityState = client.ActivityState
                })
                .ToList()
        };
    }

    private static List<ClientRow> BuildClientRows(
        IReadOnlyCollection<ReportAppointment> visits,
        IReadOnlySet<Ulid> currentClientIds,
        IReadOnlyDictionary<Ulid, IReadOnlyCollection<ClientVacationRow>> vacationsByClient,
        ReportContext context)
    {
        return visits
            .GroupBy(appointment => new { appointment.ClientId, appointment.ClientName, appointment.SourceName })
            .Select(group =>
            {
                var ordered = group.OrderBy(appointment => appointment.StartUtc).ToList();
                var intervals = ordered.Zip(ordered.Skip(1), (previous, current) => Convert.ToDecimal((current.StartLocal - previous.StartLocal).TotalDays)).ToList();
                var last = ordered[^1];
                var vacations = vacationsByClient.GetValueOrDefault(group.Key.ClientId, []);
                var reportEnd = DateOnly.FromDateTime(context.EndLocal);
                var lastVisitDate = DateOnly.FromDateTime(last.StartLocal);
                var isOnVacation = vacations.Any(vacation => vacation.StartDate <= reportEnd && vacation.EndDate >= reportEnd);
                var vacationDaysSinceLastVisit = CountVacationDays(vacations, lastVisitDate.AddDays(1), reportEnd);
                var effectiveDaysSinceLastVisit = Math.Max(0, reportEnd.DayNumber - lastVisitDate.DayNumber - vacationDaysSinceLastVisit);
                var activityState = currentClientIds.Contains(group.Key.ClientId)
                    ? "active"
                    : isOnVacation
                        ? "on-vacation"
                        : effectiveDaysSinceLastVisit > LostAfterDays
                            ? "lost"
                            : effectiveDaysSinceLastVisit > AtRiskAfterDays
                                ? "at-risk"
                                : "inactive";

                return new ClientRow(
                    group.Key.ClientId,
                    group.Key.ClientName,
                    group.Key.SourceName,
                    ordered[0].StartLocal,
                    last.StartUtc,
                    ordered.Count,
                    group.Sum(appointment => appointment.Price),
                    intervals.Count == 0 ? null : intervals.Average(),
                    activityState);
            })
            .ToList();
    }

    private static int CountVacationDays(
        IReadOnlyCollection<ClientVacationRow> vacations,
        DateOnly start,
        DateOnly end)
    {
        if (end < start)
        {
            return 0;
        }

        var days = new HashSet<int>();
        foreach (var vacation in vacations)
        {
            var overlapStart = vacation.StartDate > start ? vacation.StartDate : start;
            var overlapEnd = vacation.EndDate < end ? vacation.EndDate : end;
            for (var date = overlapStart; date <= overlapEnd; date = date.AddDays(1))
            {
                days.Add(date.DayNumber);
            }
        }

        return days.Count;
    }

    private static List<ClientActivityTrendDto> BuildTrend(
        ReportContext context,
        IReadOnlyCollection<ReportAppointment> visits,
        IReadOnlyCollection<ClientRow> clients)
    {
        return ReportBuckets.Starts(context).Select(bucketStart =>
        {
            var rawBucketEndExclusive = ReportBuckets.EndExclusive(bucketStart, context.GroupBy);
            var reportBucketStart = bucketStart < context.StartLocal ? context.StartLocal : bucketStart;
            var reportBucketEndExclusive = rawBucketEndExclusive > context.EndExclusiveLocal
                ? context.EndExclusiveLocal
                : rawBucketEndExclusive;
            var bucketVisits = visits
                .Where(appointment => appointment.StartLocal >= reportBucketStart && appointment.StartLocal < reportBucketEndExclusive)
                .ToList();
            return new ClientActivityTrendDto
            {
                StartDate = reportBucketStart,
                EndDate = reportBucketEndExclusive.AddDays(-1),
                AcquiredClients = clients.Count(client => client.FirstVisitLocal >= reportBucketStart && client.FirstVisitLocal < reportBucketEndExclusive),
                ActiveClients = bucketVisits.Select(appointment => appointment.ClientId).Distinct().Count(),
                Visits = bucketVisits.Count
            };
        }).ToList();
    }

    private sealed record ClientRow(
        Ulid ClientId,
        string ClientName,
        string SourceName,
        DateTime FirstVisitLocal,
        DateTime LastVisitAtUtc,
        int Visits,
        decimal Value,
        decimal? AverageIntervalDays,
        string ActivityState);

    private sealed record ClientVacationRow(Ulid ClientId, DateOnly StartDate, DateOnly EndDate);
}
