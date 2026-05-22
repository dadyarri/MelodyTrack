using FastEndpoints;
using MelodyTrack.Backend.Api.Dashboard.Requests;
using MelodyTrack.Backend.Api.Dashboard.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Dashboard.Endpoints;

public class GetAppointmentsAnalyticsEndpoint(AppDbContext db, IRecurringAppointmentMaterializer recurringAppointmentMaterializer)
    : Ep.Req<GetAppointmentsAnalyticsRequest>.Res<Results<Ok<GetAppointmentsAnalyticsResponse>, UnauthorizedHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Get("/dashboard/appointments");
    }

    public override async Task<Results<Ok<GetAppointmentsAnalyticsResponse>, UnauthorizedHttpResult, ProblemDetails>> ExecuteAsync(
        GetAppointmentsAnalyticsRequest req,
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

        await recurringAppointmentMaterializer.EnsureAppointmentsGeneratedAsync(rangeStartUtc, rangeEndExclusiveUtc.AddTicks(-1), ct);

        var appointments = await db.Appointments
            .AsNoTracking()
            .Where(e => !e.IsDeleted && e.StartDate >= rangeStartUtc && e.StartDate < rangeEndExclusiveUtc)
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
                StartDateUtc = e.StartDate,
                EndDateUtc = e.EndDate,
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

        var priceLookup = servicePrices
            .GroupBy(e => e.ServiceId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(item => item.EffectiveDate).ToList());

        var appointmentValues = appointments
            .Select(appointment =>
            {
                var localStart = TimeZoneInfo.ConvertTimeFromUtc(appointment.StartDateUtc, timezone);
                var localEnd = TimeZoneInfo.ConvertTimeFromUtc(appointment.EndDateUtc, timezone);

                return new AppointmentValue
                {
                    AppointmentId = appointment.AppointmentId,
                    ClientId = appointment.ClientId,
                    ClientDisplayName = $"{appointment.ClientLastName} {appointment.ClientFirstName}".Trim(),
                    ServiceId = appointment.ServiceId,
                    ServiceName = appointment.ServiceName,
                    ProviderId = appointment.ProviderId,
                    ProviderDisplayName = appointment.ProviderId is null
                        ? "Без преподавателя"
                        : $"{appointment.ProviderLastName} {appointment.ProviderFirstName}".Trim(),
                    StartDateUtc = appointment.StartDateUtc,
                    EndDateUtc = appointment.EndDateUtc,
                    LocalStart = localStart,
                    LocalEnd = localEnd,
                    Status = appointment.Status,
                    DurationHours = Convert.ToDecimal((localEnd - localStart).TotalHours),
                    Price = appointment.Status.CountsAsRevenue()
                        ? ResolveAppointmentPrice(appointment.ServiceId, appointment.StartDateUtc, priceLookup)
                        : 0m
                };
            })
            .ToList();

        var providerIds = appointmentValues
            .Where(e => e.ProviderId is not null)
            .Select(e => e.ProviderId!.Value)
            .Distinct()
            .ToList();

        var workingHours = providerIds.Count == 0
            ? []
            : await db.UserWorkingHoursDays
                .AsNoTracking()
                .Where(e => providerIds.Contains(e.UserId))
                .Select(e => new WorkingHoursRow
                {
                    UserId = e.UserId,
                    DayOfWeek = e.DayOfWeek,
                    IsWorkingDay = e.IsWorkingDay,
                    StartMinuteOfDay = e.StartMinuteOfDay,
                    EndMinuteOfDay = e.EndMinuteOfDay
                })
                .ToListAsync(ct);

        var vacations = providerIds.Count == 0
            ? []
            : await db.UserVacations
                .AsNoTracking()
                .Where(e => providerIds.Contains(e.UserId) && e.EndDate >= DateOnly.FromDateTime(rangeStartLocal) && e.StartDate < DateOnly.FromDateTime(rangeEndExclusiveLocal))
                .Select(e => new VacationRow
                {
                    UserId = e.UserId,
                    StartDate = e.StartDate,
                    EndDate = e.EndDate
                })
                .ToListAsync(ct);

        var workingHoursLookup = workingHours
            .GroupBy(e => e.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<UserWorkingHoursDaySnapshot>)group
                    .Select(item => new UserWorkingHoursDaySnapshot(item.DayOfWeek, item.IsWorkingDay, item.StartMinuteOfDay, item.EndMinuteOfDay))
                    .OrderBy(item => item.DayOfWeek)
                    .ToList());

        var vacationsLookup = vacations
            .GroupBy(e => e.UserId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<UserVacationSnapshot>)group
                    .Select(item => new UserVacationSnapshot(Ulid.Empty, item.StartDate, item.EndDate))
                    .OrderBy(item => item.StartDate)
                    .ToList());

        var teacherAvailability = providerIds.ToDictionary(
            userId => userId,
            userId => new UserAvailabilitySnapshot(
                userId,
                workingHoursLookup.GetValueOrDefault(userId, UserAvailabilityService.GetDefaultWorkingHours()),
                vacationsLookup.GetValueOrDefault(userId, [])));

        var dailyAvailableHours = new Dictionary<DateTime, decimal>();
        var hourlyAvailableHours = new Dictionary<int, decimal>();
        var teacherAvailableHours = new Dictionary<Ulid, decimal>();
        var teacherWorkingDays = new Dictionary<Ulid, int>();

        foreach (var (teacherId, availability) in teacherAvailability)
        {
            decimal teacherAvailableTotalHours = 0m;
            var workingDaysCount = 0;

            for (var day = rangeStartLocal; day < rangeEndExclusiveLocal; day = day.AddDays(1))
            {
                if (availability.Vacations.Any(vacation => vacation.StartDate <= DateOnly.FromDateTime(day) && vacation.EndDate >= DateOnly.FromDateTime(day)))
                {
                    continue;
                }

                var workingDay = availability.WorkingHours.FirstOrDefault(item => item.DayOfWeek == day.DayOfWeek);
                if (workingDay is null || !workingDay.IsWorkingDay)
                {
                    continue;
                }

                var dayStart = day.AddMinutes(workingDay.StartMinuteOfDay);
                var dayEnd = day.AddMinutes(workingDay.EndMinuteOfDay);
                if (dayEnd <= dayStart)
                {
                    continue;
                }

                workingDaysCount++;
                AddIntervalToDayBuckets(dayStart, dayEnd, dailyAvailableHours);
                AddIntervalToHourBuckets(dayStart, dayEnd, hourlyAvailableHours);
                teacherAvailableTotalHours += Convert.ToDecimal((dayEnd - dayStart).TotalHours);
            }

            teacherAvailableHours[teacherId] = teacherAvailableTotalHours;
            teacherWorkingDays[teacherId] = workingDaysCount;
        }

        var dailyTakenHours = new Dictionary<DateTime, decimal>();
        var hourlyTakenHours = new Dictionary<int, decimal>();

        foreach (var appointment in appointmentValues.Where(IsOccupiedAppointment))
        {
            AddIntervalToDayBuckets(appointment.LocalStart, appointment.LocalEnd, dailyTakenHours);
            AddIntervalToHourBuckets(appointment.LocalStart, appointment.LocalEnd, hourlyTakenHours);
        }

        var totalAppointmentsCount = appointmentValues.Count;
        var plannedAppointmentsCount = appointmentValues.Count(e => e.Status == AppointmentStatus.Planned);
        var completedAppointmentsCount = appointmentValues.Count(e => e.Status == AppointmentStatus.Completed);
        var cancelledAppointmentsCount = appointmentValues.Count(e => e.Status == AppointmentStatus.Cancelled);
        var burnedAppointmentsCount = appointmentValues.Count(e => e.Status == AppointmentStatus.Burned);
        var totalRevenue = appointmentValues.Where(e => e.Status.CountsAsRevenue()).Sum(e => e.Price);
        var takenHours = appointmentValues.Where(IsOccupiedAppointment).Sum(e => e.DurationHours);
        var workedHours = appointmentValues.Where(e => e.Status == AppointmentStatus.Completed).Sum(e => e.DurationHours);
        var availableHours = teacherAvailableHours.Values.Sum();
        var freeHours = Math.Max(0m, availableHours - takenHours);

        var statuses = BuildStatuses(appointmentValues, totalAppointmentsCount);
        var dailyLoad = BuildDailyLoad(appointmentValues, rangeStartLocal, rangeEndExclusiveLocal, dailyTakenHours, dailyAvailableHours);
        var hours = BuildHourlyAnalytics(appointmentValues, hourlyTakenHours, hourlyAvailableHours);
        var teachers = BuildTeacherAnalytics(appointmentValues, teacherAvailableHours, teacherWorkingDays);
        var burnedClients = BuildBurnedClients(appointmentValues);
        var averageGapBetweenServicesHours = CalculateAverageGapBetweenServicesHours(appointmentValues.Where(e => e.ProviderId is not null));

        return TypedResults.Ok(new GetAppointmentsAnalyticsResponse
        {
            StartDate = rangeStartLocal,
            EndDate = req.End.Date,
            TotalAppointmentsCount = totalAppointmentsCount,
            PlannedAppointmentsCount = plannedAppointmentsCount,
            CompletedAppointmentsCount = completedAppointmentsCount,
            CancelledAppointmentsCount = cancelledAppointmentsCount,
            BurnedAppointmentsCount = burnedAppointmentsCount,
            BurnedShare = CalculateShare(burnedAppointmentsCount, totalAppointmentsCount),
            CancellationShare = CalculateShare(cancelledAppointmentsCount, totalAppointmentsCount),
            TotalRevenue = totalRevenue,
            TakenHours = takenHours,
            WorkedHours = workedHours,
            AvailableHours = availableHours,
            FreeHours = freeHours,
            LoadPercentage = CalculateRatioPercent(takenHours, availableHours),
            ActiveTeachersCount = teacherAvailability.Count,
            AverageCompletedAppointmentsPerTeacher = teacherAvailability.Count == 0 ? null : completedAppointmentsCount / (decimal)teacherAvailability.Count,
            AverageGapBetweenServicesHours = averageGapBetweenServicesHours,
            Statuses = statuses,
            DailyLoad = dailyLoad,
            Hours = hours,
            Teachers = teachers,
            BurnedClients = burnedClients
        });
    }

    private static List<AppointmentStatusCountDto> BuildStatuses(IReadOnlyCollection<AppointmentValue> appointments, int totalAppointmentsCount)
    {
        return
        [
            CreateStatusDto(AppointmentStatus.Planned, appointments.Count(e => e.Status == AppointmentStatus.Planned), totalAppointmentsCount),
            CreateStatusDto(AppointmentStatus.Completed, appointments.Count(e => e.Status == AppointmentStatus.Completed), totalAppointmentsCount),
            CreateStatusDto(AppointmentStatus.Cancelled, appointments.Count(e => e.Status == AppointmentStatus.Cancelled), totalAppointmentsCount),
            CreateStatusDto(AppointmentStatus.Burned, appointments.Count(e => e.Status == AppointmentStatus.Burned), totalAppointmentsCount)
        ];
    }

    private static AppointmentStatusCountDto CreateStatusDto(AppointmentStatus status, int count, int totalAppointmentsCount)
    {
        return new AppointmentStatusCountDto
        {
            Status = status.ToApiKey(),
            Count = count,
            Share = CalculateShare(count, totalAppointmentsCount)
        };
    }

    private static List<AppointmentLoadByDayDto> BuildDailyLoad(
        IReadOnlyCollection<AppointmentValue> appointments,
        DateTime rangeStartLocal,
        DateTime rangeEndExclusiveLocal,
        IReadOnlyDictionary<DateTime, decimal> dailyTakenHours,
        IReadOnlyDictionary<DateTime, decimal> dailyAvailableHours)
    {
        var result = new List<AppointmentLoadByDayDto>();

        for (var day = rangeStartLocal; day < rangeEndExclusiveLocal; day = day.AddDays(1))
        {
            var dayAppointments = appointments.Where(e => e.LocalStart.Date == day.Date).ToList();
            var appointmentsCount = dayAppointments.Count;
            var cancelledCount = dayAppointments.Count(e => e.Status == AppointmentStatus.Cancelled);
            var burnedCount = dayAppointments.Count(e => e.Status == AppointmentStatus.Burned);
            var availableHours = dailyAvailableHours.GetValueOrDefault(day.Date, 0m);
            var takenHours = dailyTakenHours.GetValueOrDefault(day.Date, 0m);

            result.Add(new AppointmentLoadByDayDto
            {
                Date = day.Date,
                AppointmentsCount = appointmentsCount,
                ServicesProvidedCount = dayAppointments.Count(e => e.Status == AppointmentStatus.Completed),
                CompletedAppointmentsCount = dayAppointments.Count(e => e.Status == AppointmentStatus.Completed),
                CancelledAppointmentsCount = cancelledCount,
                BurnedAppointmentsCount = burnedCount,
                UniqueClientsCount = dayAppointments.Select(e => e.ClientId).Distinct().Count(),
                CompletedUniqueClientsCount = dayAppointments.Where(e => e.Status == AppointmentStatus.Completed).Select(e => e.ClientId).Distinct().Count(),
                Revenue = dayAppointments.Where(e => e.Status.CountsAsRevenue()).Sum(e => e.Price),
                TakenHours = takenHours,
                AvailableHours = availableHours,
                FreeHours = Math.Max(0m, availableHours - takenHours),
                LoadPercentage = CalculateRatioPercent(takenHours, availableHours),
                BurnedShare = CalculateShare(burnedCount, appointmentsCount),
                CancellationShare = CalculateShare(cancelledCount, appointmentsCount)
            });
        }

        return result;
    }

    private static List<AppointmentHourAnalyticsDto> BuildHourlyAnalytics(
        IReadOnlyCollection<AppointmentValue> appointments,
        IReadOnlyDictionary<int, decimal> hourlyTakenHours,
        IReadOnlyDictionary<int, decimal> hourlyAvailableHours)
    {
        return Enumerable.Range(0, 24)
            .Select(hour =>
            {
                var hourAppointments = appointments.Where(e => e.LocalStart.Hour == hour).ToList();
                var appointmentsCount = hourAppointments.Count;
                var cancelledCount = hourAppointments.Count(e => e.Status == AppointmentStatus.Cancelled);
                var burnedCount = hourAppointments.Count(e => e.Status == AppointmentStatus.Burned);
                var availableHours = hourlyAvailableHours.GetValueOrDefault(hour, 0m);
                var takenHours = hourlyTakenHours.GetValueOrDefault(hour, 0m);

                return new AppointmentHourAnalyticsDto
                {
                    Hour = hour,
                    AppointmentsCount = appointmentsCount,
                    PlannedAppointmentsCount = hourAppointments.Count(e => e.Status == AppointmentStatus.Planned),
                    CompletedAppointmentsCount = hourAppointments.Count(e => e.Status == AppointmentStatus.Completed),
                    CancelledAppointmentsCount = cancelledCount,
                    BurnedAppointmentsCount = burnedCount,
                    UniqueClientsCount = hourAppointments.Select(e => e.ClientId).Distinct().Count(),
                    Revenue = hourAppointments.Where(e => e.Status.CountsAsRevenue()).Sum(e => e.Price),
                    TakenHours = takenHours,
                    AvailableHours = availableHours,
                    FreeHours = Math.Max(0m, availableHours - takenHours),
                    LoadPercentage = CalculateRatioPercent(takenHours, availableHours),
                    CancellationRate = CalculateShare(cancelledCount, appointmentsCount),
                    BurnedShare = CalculateShare(burnedCount, appointmentsCount)
                };
            })
            .ToList();
    }

    private static List<TeacherAppointmentsAnalyticsDto> BuildTeacherAnalytics(
        IReadOnlyCollection<AppointmentValue> appointments,
        IReadOnlyDictionary<Ulid, decimal> teacherAvailableHours,
        IReadOnlyDictionary<Ulid, int> teacherWorkingDays)
    {
        return appointments
            .GroupBy(e => new { e.ProviderId, e.ProviderDisplayName })
            .Select(group =>
            {
                var totalAppointmentsCount = group.Count();
                var completedAppointmentsCount = group.Count(e => e.Status == AppointmentStatus.Completed);
                var cancelledAppointmentsCount = group.Count(e => e.Status == AppointmentStatus.Cancelled);
                var burnedAppointmentsCount = group.Count(e => e.Status == AppointmentStatus.Burned);
                var revenue = group.Where(e => e.Status.CountsAsRevenue()).Sum(e => e.Price);
                var workedHours = group.Where(e => e.Status == AppointmentStatus.Completed).Sum(e => e.DurationHours);
                var occupiedHours = group.Where(IsOccupiedAppointment).Sum(e => e.DurationHours);
                var completedAppointments = group.Where(e => e.Status == AppointmentStatus.Completed).ToList();
                var availableHours = group.Key.ProviderId is { } teacherId
                    ? teacherAvailableHours.GetValueOrDefault(teacherId, 0m)
                    : 0m;
                var workingDaysCount = group.Key.ProviderId is { } workingTeacherId
                    ? teacherWorkingDays.GetValueOrDefault(workingTeacherId, 0)
                    : 0;

                return new TeacherAppointmentsAnalyticsDto
                {
                    TeacherId = group.Key.ProviderId,
                    TeacherDisplayName = group.Key.ProviderDisplayName,
                    TotalAppointmentsCount = totalAppointmentsCount,
                    PlannedAppointmentsCount = group.Count(e => e.Status == AppointmentStatus.Planned),
                    CompletedAppointmentsCount = completedAppointmentsCount,
                    CancelledAppointmentsCount = cancelledAppointmentsCount,
                    BurnedAppointmentsCount = burnedAppointmentsCount,
                    UniqueClientsCount = group.Where(e => e.Status == AppointmentStatus.Completed).Select(e => e.ClientId).Distinct().Count(),
                    WorkingDaysCount = workingDaysCount,
                    Revenue = revenue,
                    WorkedHours = workedHours,
                    OccupiedHours = occupiedHours,
                    AvailableHours = availableHours,
                    FreeHours = Math.Max(0m, availableHours - occupiedHours),
                    LoadPercentage = CalculateRatioPercent(occupiedHours, availableHours),
                    DowntimeShare = CalculateRatioPercent(Math.Max(0m, availableHours - occupiedHours), availableHours),
                    CancellationShare = CalculateShare(cancelledAppointmentsCount, totalAppointmentsCount),
                    BurnedShare = CalculateShare(burnedAppointmentsCount, totalAppointmentsCount),
                    RevenuePerWorkedHour = workedHours == 0 ? null : revenue / workedHours,
                    RevenuePerOccupiedHour = occupiedHours == 0 ? null : revenue / occupiedHours,
                    AverageCompletedAppointmentsPerWorkingDay = workingDaysCount == 0 ? null : completedAppointmentsCount / (decimal)workingDaysCount,
                    AverageGapBetweenServicesHours = CalculateAverageGapBetweenServicesHours(completedAppointments),
                    TopServices = group
                        .GroupBy(e => new { e.ServiceId, e.ServiceName })
                        .Select(serviceGroup => new TeacherServiceAnalyticsDto
                        {
                            ServiceId = serviceGroup.Key.ServiceId,
                            ServiceName = serviceGroup.Key.ServiceName,
                            CompletedAppointmentsCount = serviceGroup.Count(e => e.Status == AppointmentStatus.Completed),
                            RevenueCountedAppointmentsCount = serviceGroup.Count(e => e.Status.CountsAsRevenue()),
                            Revenue = serviceGroup.Where(e => e.Status.CountsAsRevenue()).Sum(e => e.Price),
                            CompletedShare = completedAppointmentsCount == 0
                                ? null
                                : serviceGroup.Count(e => e.Status == AppointmentStatus.Completed) / (decimal)completedAppointmentsCount * 100m
                        })
                        .OrderByDescending(e => e.CompletedAppointmentsCount)
                        .ThenByDescending(e => e.Revenue)
                        .ThenBy(e => e.ServiceName)
                        .Take(5)
                        .ToList()
                };
            })
            .OrderByDescending(e => e.Revenue)
            .ThenByDescending(e => e.TotalAppointmentsCount)
            .ThenBy(e => e.TeacherDisplayName)
            .ToList();
    }

    private static List<BurnedClientAnalyticsDto> BuildBurnedClients(IReadOnlyCollection<AppointmentValue> appointments)
    {
        return appointments
            .GroupBy(e => new { e.ClientId, e.ClientDisplayName })
            .Select(group =>
            {
                var totalAppointmentsCount = group.Count();
                var burnedAppointmentsCount = group.Count(e => e.Status == AppointmentStatus.Burned);

                return new BurnedClientAnalyticsDto
                {
                    ClientId = group.Key.ClientId,
                    ClientDisplayName = group.Key.ClientDisplayName,
                    TotalAppointmentsCount = totalAppointmentsCount,
                    BurnedAppointmentsCount = burnedAppointmentsCount,
                    BurnedShare = CalculateShare(burnedAppointmentsCount, totalAppointmentsCount)
                };
            })
            .Where(e => e.BurnedAppointmentsCount > 0)
            .OrderByDescending(e => e.BurnedShare ?? decimal.MinValue)
            .ThenByDescending(e => e.BurnedAppointmentsCount)
            .ThenByDescending(e => e.TotalAppointmentsCount)
            .Take(20)
            .ToList();
    }

    private static decimal? CalculateAverageGapBetweenServicesHours(IEnumerable<AppointmentValue> appointments)
    {
        var orderedAppointments = appointments
            .OrderBy(e => e.ProviderId)
            .ThenBy(e => e.StartDateUtc)
            .ThenBy(e => e.AppointmentId)
            .GroupBy(e => e.ProviderId)
            .ToList();

        var gaps = new List<decimal>();

        foreach (var group in orderedAppointments)
        {
            AppointmentValue? previous = null;

            foreach (var appointment in group)
            {
                if (previous is not null)
                {
                    var gapHours = Convert.ToDecimal((appointment.StartDateUtc - previous.EndDateUtc).TotalHours);
                    if (gapHours > 0)
                    {
                        gaps.Add(gapHours);
                    }
                }

                previous = appointment;
            }
        }

        return gaps.Count == 0 ? null : gaps.Average();
    }

    private static void AddIntervalToDayBuckets(DateTime start, DateTime end, IDictionary<DateTime, decimal> buckets)
    {
        var cursor = start;
        while (cursor < end)
        {
            var dayEnd = cursor.Date.AddDays(1);
            var segmentEnd = end < dayEnd ? end : dayEnd;
            var hours = Convert.ToDecimal((segmentEnd - cursor).TotalHours);
            buckets[cursor.Date] = GetValueOrZero(buckets, cursor.Date) + hours;
            cursor = segmentEnd;
        }
    }

    private static void AddIntervalToHourBuckets(DateTime start, DateTime end, IDictionary<int, decimal> buckets)
    {
        var cursor = start;
        while (cursor < end)
        {
            var hourEnd = new DateTime(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 0, 0, cursor.Kind).AddHours(1);
            var segmentEnd = end < hourEnd ? end : hourEnd;
            var hours = Convert.ToDecimal((segmentEnd - cursor).TotalHours);
            buckets[cursor.Hour] = GetValueOrZero(buckets, cursor.Hour) + hours;
            cursor = segmentEnd;
        }
    }

    private static decimal GetValueOrZero<TKey>(IDictionary<TKey, decimal> buckets, TKey key)
        where TKey : notnull
    {
        return buckets.TryGetValue(key, out var value) ? value : 0m;
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

    private static bool IsOccupiedAppointment(AppointmentValue appointment)
    {
        return appointment.Status is AppointmentStatus.Planned or AppointmentStatus.Completed or AppointmentStatus.Burned;
    }

    private static decimal? CalculateShare(int part, int total)
    {
        return total == 0 ? null : part / (decimal)total * 100m;
    }

    private static decimal? CalculateRatioPercent(decimal numerator, decimal denominator)
    {
        return denominator == 0 ? null : numerator / denominator * 100m;
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
        public required DateTime StartDateUtc { get; set; }
        public required DateTime EndDateUtc { get; set; }
        public required AppointmentStatus Status { get; set; }
    }

    private sealed class AppointmentValue
    {
        public required Ulid AppointmentId { get; set; }
        public required Ulid ClientId { get; set; }
        public required string ClientDisplayName { get; set; }
        public required Ulid ServiceId { get; set; }
        public required string ServiceName { get; set; }
        public Ulid? ProviderId { get; set; }
        public required string ProviderDisplayName { get; set; }
        public required DateTime StartDateUtc { get; set; }
        public required DateTime EndDateUtc { get; set; }
        public required DateTime LocalStart { get; set; }
        public required DateTime LocalEnd { get; set; }
        public required AppointmentStatus Status { get; set; }
        public required decimal DurationHours { get; set; }
        public required decimal Price { get; set; }
    }

    private sealed class ServicePriceRow
    {
        public required Ulid ServiceId { get; set; }
        public required DateTime EffectiveDate { get; set; }
        public required decimal Price { get; set; }
    }

    private sealed class WorkingHoursRow
    {
        public required Ulid UserId { get; set; }
        public required DayOfWeek DayOfWeek { get; set; }
        public required bool IsWorkingDay { get; set; }
        public required int StartMinuteOfDay { get; set; }
        public required int EndMinuteOfDay { get; set; }
    }

    private sealed class VacationRow
    {
        public required Ulid UserId { get; set; }
        public required DateOnly StartDate { get; set; }
        public required DateOnly EndDate { get; set; }
    }
}
