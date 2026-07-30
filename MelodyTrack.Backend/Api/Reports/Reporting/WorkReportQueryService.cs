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
        var providerIds = context.ProviderId is { } providerId
            ? [providerId]
            : appointments.Where(item => item.ProviderId is not null).Select(item => item.ProviderId!.Value).Distinct().ToList();
        var availability = providerIds.Count == 0
            ? []
            : await availabilityService.GetAvailabilitiesAsync(providerIds, ct);
        var availableHoursByDay = CalculateAvailableHours(availability, context);
        var availableHoursByProvider = availability.ToDictionary(
            item => item.UserId,
            item => availableHoursByDay
                .Where(pair => pair.Key.ProviderId == item.UserId)
                .Sum(pair => pair.Value));

        var total = appointments.Count;
        var occupiedHours = appointments.Where(item => item.IsOccupied).Sum(item => item.DurationHours);
        var availableHours = availableHoursByDay.Values.Sum();

        return new WorkReportResponse
        {
            Context = await contextFactory.CreateDtoAsync(context, ct),
            Summary = new WorkReportSummaryDto
            {
                Appointments = total,
                Completed = appointments.Count(item => item.Status == AppointmentStatus.Completed),
                Burned = appointments.Count(item => item.Status == AppointmentStatus.Burned),
                OccupiedHours = occupiedHours,
                AvailableHours = availableHours,
                WorkloadPercent = ReportBuckets.Percent(occupiedHours, availableHours),
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
            Trend = BuildTrend(context, appointments, availableHoursByDay),
            Providers = appointments
                .GroupBy(item => new { item.ProviderId, item.ProviderName })
                .Select(group =>
                {
                    var providerAvailableHours = group.Key.ProviderId is { } id
                        ? availableHoursByProvider.GetValueOrDefault(id)
                        : 0m;
                    var providerOccupiedHours = group.Where(item => item.IsOccupied).Sum(item => item.DurationHours);
                    return new WorkProviderDto
                    {
                        ProviderId = group.Key.ProviderId,
                        ProviderName = group.Key.ProviderName,
                        Appointments = group.Count(),
                        Completed = group.Count(item => item.Status == AppointmentStatus.Completed),
                        Cancelled = group.Count(item => item.Status == AppointmentStatus.Cancelled),
                        Burned = group.Count(item => item.Status == AppointmentStatus.Burned),
                        OccupiedHours = providerOccupiedHours,
                        AvailableHours = providerAvailableHours,
                        WorkloadPercent = ReportBuckets.Percent(providerOccupiedHours, providerAvailableHours)
                    };
                })
                .OrderByDescending(item => item.OccupiedHours)
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
                    Revenue = group.Where(item => item.IsVisit).Sum(item => item.Price)
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

    private static Dictionary<(Ulid ProviderId, DateTime Date), decimal> CalculateAvailableHours(
        IReadOnlyList<UserAvailabilitySnapshot> availabilities,
        ReportContext context)
    {
        var result = new Dictionary<(Ulid ProviderId, DateTime Date), decimal>();
        foreach (var availability in availabilities)
        {
            for (var date = context.StartLocal; date < context.EndExclusiveLocal; date = date.AddDays(1))
            {
                var localDate = DateOnly.FromDateTime(date);
                if (availability.Vacations.Any(vacation => vacation.StartDate <= localDate && vacation.EndDate >= localDate))
                {
                    continue;
                }

                var workingDay = availability.WorkingHours.FirstOrDefault(day => day.DayOfWeek == date.DayOfWeek);
                if (workingDay is null || !workingDay.IsWorkingDay || workingDay.EndMinuteOfDay <= workingDay.StartMinuteOfDay)
                {
                    continue;
                }

                result[(availability.UserId, date.Date)] = (workingDay.EndMinuteOfDay - workingDay.StartMinuteOfDay) / 60m;
            }
        }

        return result;
    }

    private static List<WorkTrendDto> BuildTrend(
        ReportContext context,
        IReadOnlyList<ReportAppointment> appointments,
        IReadOnlyDictionary<(Ulid ProviderId, DateTime Date), decimal> availableHoursByDay)
    {
        return ReportBuckets.Starts(context).Select(bucketStart =>
        {
            var bucketEndExclusive = ReportBuckets.EndExclusive(bucketStart, context.GroupBy);
            var bucketAppointments = appointments
                .Where(item => item.StartLocal >= bucketStart && item.StartLocal < bucketEndExclusive)
                .ToList();
            var occupied = bucketAppointments.Where(item => item.IsOccupied).Sum(item => item.DurationHours);
            var available = availableHoursByDay
                .Where(pair => pair.Key.Date >= bucketStart && pair.Key.Date < bucketEndExclusive)
                .Sum(pair => pair.Value);
            return new WorkTrendDto
            {
                StartDate = bucketStart < context.StartLocal ? context.StartLocal : bucketStart,
                EndDate = (bucketEndExclusive > context.EndExclusiveLocal ? context.EndExclusiveLocal : bucketEndExclusive).AddDays(-1),
                Appointments = bucketAppointments.Count,
                Completed = bucketAppointments.Count(item => item.Status == AppointmentStatus.Completed),
                Cancelled = bucketAppointments.Count(item => item.Status == AppointmentStatus.Cancelled),
                Burned = bucketAppointments.Count(item => item.Status == AppointmentStatus.Burned),
                OccupiedHours = occupied,
                AvailableHours = available,
                WorkloadPercent = ReportBuckets.Percent(occupied, available)
            };
        }).ToList();
    }
}
