using MelodyTrack.Backend.Api.Reports.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;

namespace MelodyTrack.Backend.Api.Reports.Reporting;

public interface IWorkReportQueryService
{
    Task<WorkReportResponse> GetAsync(ReportContext context, CancellationToken ct);
}

public sealed class WorkReportQueryService(
    IReportContextFactory contextFactory,
    IReportAppointmentQuery appointmentQuery,
    IRecurringAppointmentMaterializer materializer,
    IUserAvailabilityService availabilityService) : IWorkReportQueryService
{
    public async Task<WorkReportResponse> GetAsync(ReportContext context, CancellationToken ct)
    {
        await materializer.EnsureAppointmentsGeneratedAsync(context.StartUtc, context.EndExclusiveUtc.AddTicks(-1), ct);
        var appointments = await appointmentQuery.LoadAsync(context, context.StartUtc, context.EndExclusiveUtc, ct);
        var contextDto = await contextFactory.CreateDtoAsync(context, ct);
        var reportProviders = context.ProviderId is { } providerId
            ? contextDto.Providers.Where(provider => provider.Id == providerId).ToList()
            : contextDto.Providers;
        var availabilities = reportProviders.Count == 0
            ? []
            : await availabilityService.GetAvailabilitiesAsync(reportProviders.Select(provider => provider.Id).ToList(), ct);
        var workingIntervals = BuildWorkingIntervals(availabilities, context);
        var totalMetrics = CalculateWorkingTime(workingIntervals, appointments, null, context.StartLocal, context.EndExclusiveLocal);
        var total = appointments.Count;

        var providerRows = reportProviders.Select(provider =>
        {
            var providerAppointments = appointments.Where(item => item.ProviderId == provider.Id).ToList();
            var metrics = CalculateWorkingTime(
                workingIntervals,
                appointments,
                provider.Id,
                context.StartLocal,
                context.EndExclusiveLocal);
            return CreateProviderRow(provider.Id, provider.DisplayName, providerAppointments, metrics);
        }).ToList();

        var unassignedAppointments = appointments.Where(item => item.ProviderId is null).ToList();
        if (unassignedAppointments.Count > 0)
        {
            providerRows.Add(CreateProviderRow(null, "Без преподавателя", unassignedAppointments, WorkingTimeMetrics.Empty));
        }

        return new WorkReportResponse
        {
            Context = contextDto,
            Summary = new WorkReportSummaryDto
            {
                Appointments = total,
                Completed = appointments.Count(item => item.Status == AppointmentStatus.Completed),
                Burned = appointments.Count(item => item.Status == AppointmentStatus.Burned),
                WorkingCapacityHours = totalMetrics.CapacityHours,
                OccupiedWorkingHours = totalMetrics.OccupiedHours,
                FreeWorkingHours = totalMetrics.FreeHours,
                UtilizationPercent = ReportBuckets.Percent(totalMetrics.OccupiedHours, totalMetrics.CapacityHours),
                CancellationPercent = ReportBuckets.Percent(appointments.Count(item => item.Status == AppointmentStatus.Cancelled), total)
            },
            Statuses = Enum.GetValues<AppointmentStatus>()
                .Select(status => new WorkStatusDto
                {
                    Status = status.ToApiKey(),
                    Count = appointments.Count(item => item.Status == status),
                    SharePercent = ReportBuckets.Percent(appointments.Count(item => item.Status == status), total)
                })
                .ToList(),
            Trend = BuildTrend(context, appointments, workingIntervals),
            Providers = providerRows
                .OrderByDescending(item => item.OccupiedWorkingHours)
                .ThenBy(item => item.ProviderName)
                .ToList(),
            Services = appointments
                .GroupBy(item => new { item.ServiceId, item.ServiceName })
                .Select(group => new WorkServiceDto
                {
                    ServiceId = group.Key.ServiceId,
                    ServiceName = group.Key.ServiceName,
                    Appointments = group.Count(),
                    Completed = group.Count(item => item.Status == AppointmentStatus.Completed),
                    Burned = group.Count(item => item.Status == AppointmentStatus.Burned),
                    Revenue = group.Where(item => item.IsValueVisit).Sum(item => item.Price)
                })
                .OrderByDescending(item => item.Appointments)
                .ThenBy(item => item.ServiceName)
                .ToList(),
            BusyHours = Enumerable.Range(0, 24)
                .Select(hour => new WorkHourDto
                {
                    Hour = hour,
                    Appointments = appointments.Count(item => item.StartLocal.Hour == hour),
                    Completed = appointments.Count(item => item.StartLocal.Hour == hour && item.Status == AppointmentStatus.Completed),
                    Cancelled = appointments.Count(item => item.StartLocal.Hour == hour && item.Status == AppointmentStatus.Cancelled)
                })
                .Where(item => item.Appointments > 0)
                .ToList()
        };
    }

    private static WorkProviderDto CreateProviderRow(
        Ulid? providerId,
        string providerName,
        IReadOnlyCollection<ReportAppointment> appointments,
        WorkingTimeMetrics metrics)
    {
        return new WorkProviderDto
        {
            ProviderId = providerId,
            ProviderName = providerName,
            Appointments = appointments.Count,
            Completed = appointments.Count(item => item.Status == AppointmentStatus.Completed),
            Cancelled = appointments.Count(item => item.Status == AppointmentStatus.Cancelled),
            Burned = appointments.Count(item => item.Status == AppointmentStatus.Burned),
            WorkingCapacityHours = metrics.CapacityHours,
            OccupiedWorkingHours = metrics.OccupiedHours,
            FreeWorkingHours = metrics.FreeHours,
            UtilizationPercent = ReportBuckets.Percent(metrics.OccupiedHours, metrics.CapacityHours)
        };
    }

    private static List<WorkingInterval> BuildWorkingIntervals(
        IReadOnlyList<UserAvailabilitySnapshot> availabilities,
        ReportContext context)
    {
        var result = new List<WorkingInterval>();
        foreach (var availability in availabilities)
        {
            for (var date = context.StartLocal; date < context.EndExclusiveLocal; date = date.AddDays(1))
            {
                var workingDay = availability.WorkingHours.FirstOrDefault(day => day.DayOfWeek == date.DayOfWeek);
                if (workingDay is null || !workingDay.IsWorkingDay || workingDay.EndMinuteOfDay <= workingDay.StartMinuteOfDay)
                {
                    continue;
                }

                var localStart = date.Date.AddMinutes(workingDay.StartMinuteOfDay);
                var localEnd = date.Date.AddMinutes(workingDay.EndMinuteOfDay);
                var startUtc = ReportBuckets.ToUtc(localStart, context.Timezone);
                var endUtc = ReportBuckets.ToUtc(localEnd, context.Timezone);
                if (endUtc > startUtc)
                {
                    var intervals = new List<(DateTime StartUtc, DateTime EndUtc)> { (startUtc, endUtc) };
                    foreach (var vacation in availability.Vacations.Where(vacation => vacation.StartDate < endUtc && vacation.EndDate > startUtc))
                    {
                        intervals = intervals.SelectMany(interval => Subtract(interval, vacation.StartDate, vacation.EndDate)).ToList();
                    }

                    result.AddRange(intervals.Select(interval =>
                        new WorkingInterval(availability.UserId, date.Date, interval.StartUtc, interval.EndUtc)));
                }
            }
        }

        return result;
    }

    private static IEnumerable<(DateTime StartUtc, DateTime EndUtc)> Subtract(
        (DateTime StartUtc, DateTime EndUtc) interval,
        DateTime blockedStartUtc,
        DateTime blockedEndUtc)
    {
        if (blockedStartUtc > interval.StartUtc)
        {
            yield return (interval.StartUtc, blockedStartUtc < interval.EndUtc ? blockedStartUtc : interval.EndUtc);
        }

        if (blockedEndUtc < interval.EndUtc)
        {
            yield return (blockedEndUtc > interval.StartUtc ? blockedEndUtc : interval.StartUtc, interval.EndUtc);
        }
    }

    private static WorkingTimeMetrics CalculateWorkingTime(
        IReadOnlyCollection<WorkingInterval> workingIntervals,
        IReadOnlyCollection<ReportAppointment> appointments,
        Ulid? providerId,
        DateTime startLocal,
        DateTime endExclusiveLocal)
    {
        var intervals = workingIntervals
            .Where(interval => interval.LocalDate >= startLocal.Date
                               && interval.LocalDate < endExclusiveLocal.Date
                               && (providerId is null || interval.ProviderId == providerId))
            .ToList();
        var capacity = intervals.Sum(interval => Hours(interval.EndUtc - interval.StartUtc));
        decimal occupied = 0m;

        foreach (var interval in intervals)
        {
            var overlaps = appointments
                .Where(appointment => appointment.ProviderId == interval.ProviderId
                                      && appointment.IsOccupied
                                      && appointment.EndUtc > interval.StartUtc
                                      && appointment.StartUtc < interval.EndUtc)
                .Select(appointment => new UtcInterval(
                    appointment.StartUtc > interval.StartUtc ? appointment.StartUtc : interval.StartUtc,
                    appointment.EndUtc < interval.EndUtc ? appointment.EndUtc : interval.EndUtc))
                .OrderBy(item => item.StartUtc)
                .ToList();
            if (overlaps.Count == 0)
            {
                continue;
            }

            var mergedStart = overlaps[0].StartUtc;
            var mergedEnd = overlaps[0].EndUtc;
            foreach (var overlap in overlaps.Skip(1))
            {
                if (overlap.StartUtc <= mergedEnd)
                {
                    if (overlap.EndUtc > mergedEnd)
                    {
                        mergedEnd = overlap.EndUtc;
                    }

                    continue;
                }

                occupied += Hours(mergedEnd - mergedStart);
                mergedStart = overlap.StartUtc;
                mergedEnd = overlap.EndUtc;
            }

            occupied += Hours(mergedEnd - mergedStart);
        }

        return new WorkingTimeMetrics(capacity, occupied);
    }

    private static List<WorkTrendDto> BuildTrend(
        ReportContext context,
        IReadOnlyList<ReportAppointment> appointments,
        IReadOnlyCollection<WorkingInterval> workingIntervals)
    {
        return ReportBuckets.Starts(context).Select(bucketStart =>
        {
            var rawBucketEndExclusive = ReportBuckets.EndExclusive(bucketStart, context.GroupBy);
            var reportBucketStart = bucketStart < context.StartLocal ? context.StartLocal : bucketStart;
            var reportBucketEndExclusive = rawBucketEndExclusive > context.EndExclusiveLocal
                ? context.EndExclusiveLocal
                : rawBucketEndExclusive;
            var bucketAppointments = appointments
                .Where(item => item.StartLocal >= reportBucketStart && item.StartLocal < reportBucketEndExclusive)
                .ToList();
            var metrics = CalculateWorkingTime(
                workingIntervals,
                appointments,
                null,
                reportBucketStart,
                reportBucketEndExclusive);
            return new WorkTrendDto
            {
                StartDate = reportBucketStart,
                EndDate = reportBucketEndExclusive.AddDays(-1),
                Appointments = bucketAppointments.Count,
                Completed = bucketAppointments.Count(item => item.Status == AppointmentStatus.Completed),
                Cancelled = bucketAppointments.Count(item => item.Status == AppointmentStatus.Cancelled),
                Burned = bucketAppointments.Count(item => item.Status == AppointmentStatus.Burned),
                WorkingCapacityHours = metrics.CapacityHours,
                OccupiedWorkingHours = metrics.OccupiedHours,
                FreeWorkingHours = metrics.FreeHours,
                UtilizationPercent = ReportBuckets.Percent(metrics.OccupiedHours, metrics.CapacityHours)
            };
        }).ToList();
    }

    private static decimal Hours(TimeSpan duration) => Convert.ToDecimal(duration.TotalHours);

    private sealed record WorkingInterval(Ulid ProviderId, DateTime LocalDate, DateTime StartUtc, DateTime EndUtc);
    private sealed record UtcInterval(DateTime StartUtc, DateTime EndUtc);
    private sealed record WorkingTimeMetrics(decimal CapacityHours, decimal OccupiedHours)
    {
        public static WorkingTimeMetrics Empty { get; } = new(0m, 0m);
        public decimal FreeHours => CapacityHours - OccupiedHours;
    }
}
