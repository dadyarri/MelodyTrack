namespace MelodyTrack.Core.Scheduling;

public sealed record SchedulingWorkingDay(DayOfWeek Day, bool IsWorkingDay, int StartMinute, int EndMinute);
public sealed record SchedulingVacation(Ulid Id, DateTime StartUtc, DateTime EndUtc);
public sealed record SchedulingBusyInterval(Ulid AppointmentId, DateTime StartUtc, DateTime EndUtc);
public sealed record SchedulingBlocker(string Code, string Message, Ulid? VacationId = null, Ulid? AppointmentId = null);

public static class SchedulingAvailability
{
    public static bool Overlaps(DateTime startUtc, DateTime endUtc, DateTime otherStartUtc, DateTime otherEndUtc) =>
        startUtc < otherEndUtc && endUtc > otherStartUtc;

    public static IReadOnlyList<DateTime> GetUtcInstants(DateTime local, TimeZoneInfo zone)
    {
        var estimate = new DateTime(local.Ticks - zone.BaseUtcOffset.Ticks, DateTimeKind.Utc);
        return new[] { zone.GetUtcOffset(estimate.AddDays(-1)), zone.GetUtcOffset(estimate), zone.GetUtcOffset(estimate.AddDays(1)) }
            .Distinct()
            .Select(offset => new DateTime(local.Ticks - offset.Ticks, DateTimeKind.Utc))
            .Where(instant => TimeZoneInfo.ConvertTimeFromUtc(instant, zone) == local)
            .Order().ToList();
    }

    public static IReadOnlyList<SchedulingBlocker> Evaluate(
        DateTime startUtc,
        DateTime endUtc,
        IReadOnlyCollection<SchedulingVacation> clientVacations,
        IReadOnlyCollection<SchedulingVacation> providerVacations,
        IReadOnlyCollection<SchedulingWorkingDay>? workingHours,
        string timezone,
        IReadOnlyCollection<SchedulingBusyInterval>? clientAppointments = null,
        IReadOnlyCollection<SchedulingBusyInterval>? providerAppointments = null)
    {
        if (startUtc.Kind != DateTimeKind.Utc || endUtc.Kind != DateTimeKind.Utc || endUtc <= startUtc)
        {
            return [new SchedulingBlocker("invalid_interval", "Начало и окончание занятия должны задавать положительный интервал в UTC.")];
        }

        var blockers = new List<SchedulingBlocker>();
        blockers.AddRange(clientVacations.Where(vacation => Overlaps(startUtc, endUtc, vacation.StartUtc, vacation.EndUtc))
            .Select(vacation => new SchedulingBlocker("client_vacation", "Клиент находится в отпуске.", vacation.Id)));
        blockers.AddRange(providerVacations.Where(vacation => Overlaps(startUtc, endUtc, vacation.StartUtc, vacation.EndUtc))
            .Select(vacation => new SchedulingBlocker("provider_vacation", "Преподаватель находится в отпуске.", vacation.Id)));

        if (workingHours is not null)
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(timezone);
            var localStart = TimeZoneInfo.ConvertTimeFromUtc(startUtc, zone);
            var day = workingHours.SingleOrDefault(item => item.Day == localStart.DayOfWeek);
            if (day is null || !day.IsWorkingDay)
            {
                blockers.Add(new SchedulingBlocker("provider_day_off", "Выбранный день является выходным преподавателя."));
            }
            else if (!GetWorkingIntervals(localStart.Date, day.StartMinute, day.EndMinute, zone)
                .Any(interval => startUtc >= interval.StartUtc && endUtc <= interval.EndUtc))
            {
                blockers.Add(new SchedulingBlocker("provider_working_hours", "Занятие выходит за рабочие часы преподавателя."));
            }
        }

        blockers.AddRange((clientAppointments ?? []).Where(item => Overlaps(startUtc, endUtc, item.StartUtc, item.EndUtc))
            .Select(item => new SchedulingBlocker("client_busy", "У клиента уже есть занятие в это время.", AppointmentId: item.AppointmentId)));
        blockers.AddRange((providerAppointments ?? []).Where(item => Overlaps(startUtc, endUtc, item.StartUtc, item.EndUtc))
            .Select(item => new SchedulingBlocker("provider_busy", "У преподавателя уже есть занятие в это время.", AppointmentId: item.AppointmentId)));
        return blockers;
    }
    public static IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> GetWorkingIntervals(
        DateTime localDate, int startMinute, int endMinute, TimeZoneInfo zone)
    {
        if (endMinute <= startMinute) return [];
        var localStart = DateTime.SpecifyKind(localDate.Date.AddMinutes(startMinute), DateTimeKind.Unspecified);
        var localEnd = DateTime.SpecifyKind(localDate.Date.AddMinutes(endMinute), DateTimeKind.Unspecified);
        DateTime Boundary(DateTime local, bool first)
        {
            var instants = GetUtcInstants(local, zone);
            while (instants.Count == 0)
            {
                local = local.AddMinutes(1);
                instants = GetUtcInstants(local, zone);
            }
            return first ? instants[0] : instants[^1];
        }
        var startUtc = Boundary(localStart, true);
        var endUtc = Boundary(localEnd, false);
        if (endUtc <= startUtc) return [];
        if (zone.GetUtcOffset(startUtc) == zone.GetUtcOffset(endUtc)) return [(startUtc, endUtc)];

        // Working times are minute-based. On a clock-change day the same wall-time
        // window can occur twice, separated by minutes outside the working window.
        var result = new List<(DateTime StartUtc, DateTime EndUtc)>();
        DateTime? segmentStart = null;
        for (var cursor = startUtc; cursor < endUtc; cursor = cursor.AddMinutes(1))
        {
            var local = TimeZoneInfo.ConvertTimeFromUtc(cursor, zone);
            var working = local >= localStart && local < localEnd;
            if (working) segmentStart ??= cursor;
            else if (segmentStart is { } segment)
            {
                result.Add((segment, cursor));
                segmentStart = null;
            }
        }
        if (segmentStart is { } last) result.Add((last, endUtc));
        return result;
    }

}
