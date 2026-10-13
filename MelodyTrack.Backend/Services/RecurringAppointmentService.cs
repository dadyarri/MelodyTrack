using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Core.Scheduling;

namespace MelodyTrack.Backend.Services;

public interface IRecurringAppointmentService
{
    IEnumerable<Appointment> GetAppointmentsForRule(AppointmentRecurrenceRule rule, DateTime now);
    IEnumerable<Appointment> GetAppointmentsForRule(AppointmentRecurrenceRule rule, DateTime rangeStart, DateTime rangeEnd);
    long GetOrdinal(AppointmentRecurrenceRule rule, DateTime startUtc);
    Appointment? GetOccurrence(AppointmentRecurrenceRule rule, long ordinal);
}

public sealed class RecurringAppointmentService : IRecurringAppointmentService
{
    public IEnumerable<Appointment> GetAppointmentsForRule(AppointmentRecurrenceRule rule, DateTime now) =>
        GetAppointmentsForRule(rule, now, now.Date.AddDays(7).AddTicks(-1));

    public IEnumerable<Appointment> GetAppointmentsForRule(AppointmentRecurrenceRule rule, DateTime rangeStart, DateTime rangeEnd)
    {
        if (rangeEnd < rangeStart || rule.DurationMinutes <= 0)
        {
            yield break;
        }
        var localRuleStart = DateTimeUtils.ConvertDateToTimezone(rule.StartDate, rule.Timezone);
        var localRangeStart = DateTimeUtils.ConvertDateToTimezone(rangeStart, rule.Timezone);
        var localRangeEnd = DateTimeUtils.ConvertDateToTimezone(rangeEnd, rule.Timezone);
        var firstDate = localRangeStart.Date > localRuleStart.Date ? localRangeStart.Date : localRuleStart.Date;
        for (var date = firstDate; date <= localRangeEnd.Date; date = date.AddDays(1))
        {
            if (!IsEnabled(rule, date))
            {
                continue;
            }
            var localStart = DateTime.SpecifyKind(date.Add(localRuleStart.TimeOfDay), DateTimeKind.Unspecified);
            var zone = TimeZoneInfo.FindSystemTimeZoneById(rule.Timezone);
            var instants = SchedulingAvailability.GetUtcInstants(localStart, zone);
            if (instants.Count == 0)
            {
                continue;
            }
            var startUtc = instants[^1];
            if (startUtc < rule.StartDate || startUtc < rangeStart || startUtc > rangeEnd
                || rule.EndDate is { } endUtc && startUtc > endUtc)
            {
                continue;
            }
            yield return Create(rule, startUtc, GetOrdinal(rule, startUtc));
        }
    }

    public long GetOrdinal(AppointmentRecurrenceRule rule, DateTime startUtc)
    {
        var first = DateTimeUtils.ConvertDateToTimezone(rule.StartDate, rule.Timezone);
        var local = DateTimeUtils.ConvertDateToTimezone(startUtc, rule.Timezone);
        var days = (local.Date - first.Date).Days;
        return rule.RecurrenceType.Type switch
        {
            AppointmentRecurrenceType.Daily => days,
            AppointmentRecurrenceType.Monthly => (local.Year - first.Year) * 12L + local.Month - first.Month,
            AppointmentRecurrenceType.Weekly => days,
            _ => throw new InvalidOperationException("Unsupported appointment recurrence type.")
        };
    }

    public Appointment? GetOccurrence(AppointmentRecurrenceRule rule, long ordinal)
    {
        if (ordinal < 0 || rule.DurationMinutes <= 0)
        {
            return null;
        }
        var first = DateTimeUtils.ConvertDateToTimezone(rule.StartDate, rule.Timezone);
        DateTime date;
        switch (rule.RecurrenceType.Type)
        {
            case AppointmentRecurrenceType.Daily:
                date = first.Date.AddDays(ordinal);
                break;
            case AppointmentRecurrenceType.Monthly:
                var month = new DateTime(first.Year, first.Month, 1).AddMonths(checked((int)ordinal));
                var day = rule.RecurrencePattern ?? 0;
                if (day < 1 || day > DateTime.DaysInMonth(month.Year, month.Month)) return null;
                date = month.AddDays(day - 1);
                break;
            case AppointmentRecurrenceType.Weekly:
                date = first.Date.AddDays(ordinal);
                if (!IsEnabled(rule, date)) return null;
                break;
            default:
                return null;
        }
        var localStart = DateTime.SpecifyKind(date.Add(first.TimeOfDay), DateTimeKind.Unspecified);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(rule.Timezone);
        var instants = SchedulingAvailability.GetUtcInstants(localStart, zone);
        if (instants.Count == 0) return null;
        var utc = instants[^1];
        return utc < rule.StartDate || rule.EndDate is { } endUtc && utc > endUtc ? null : Create(rule, utc, ordinal);
    }

    public static int DayFlag(DayOfWeek day) => day == DayOfWeek.Sunday ? 64 : 1 << ((int)day - 1);

    private static bool IsEnabled(AppointmentRecurrenceRule rule, DateTime date) => rule.RecurrenceType.Type switch
    {
        AppointmentRecurrenceType.Daily => true,
        AppointmentRecurrenceType.Weekly => ((rule.RecurrencePattern ?? 0) & DayFlag(date.DayOfWeek)) != 0,
        AppointmentRecurrenceType.Monthly => date.Day == rule.RecurrencePattern,
        _ => false
    };

    private static Appointment Create(AppointmentRecurrenceRule rule, DateTime startUtc, long ordinal) => new()
    {
        Id = Ulid.NewUlid(),
        Client = rule.Client,
        Service = rule.Service,
        Provider = rule.Provider,
        StartDate = startUtc,
        EndDate = startUtc.AddMinutes(rule.DurationMinutes),
        Status = AppointmentStatus.Planned,
        IsDeleted = false,
        RecurringRule = rule,
        RecurringRuleId = rule.Id,
        RecurrenceOrdinal = ordinal
    };
}
