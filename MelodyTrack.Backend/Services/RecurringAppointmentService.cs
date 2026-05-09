using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Services;

/// <summary>
///     Service for generating recurring appointments based on recurrence rules.
/// </summary>
public interface IRecurringAppointmentService
{
    /// <summary>
    ///     Generates appointment instances for the given recurrence rule based on the current date/time.
    /// </summary>
    /// <param name="rule">The recurrence rule to process</param>
    /// <param name="now">The current date/time to check against</param>
    /// <returns>A list of appointments to create for the current period</returns>
    IEnumerable<Appointment> GetAppointmentsForRule(AppointmentRecurrenceRule rule, DateTime now);
    IEnumerable<Appointment> GetAppointmentsForRule(AppointmentRecurrenceRule rule, DateTime rangeStart, DateTime rangeEnd);
}

/// <inheritdoc />
public class RecurringAppointmentService : IRecurringAppointmentService
{
    /// <inheritdoc />
    public IEnumerable<Appointment> GetAppointmentsForRule(AppointmentRecurrenceRule rule, DateTime now)
    {
        return rule.RecurrenceType.Type switch
        {
            AppointmentRecurrenceType.Daily => GetAppointmentsForRule(rule, now, GetEndOfDay(now.Date.AddDays(6))),
            AppointmentRecurrenceType.Weekly => GetAppointmentsForRule(rule, now, GetEndOfDay(now.Date.AddDays(7))),
            AppointmentRecurrenceType.Monthly => GetAppointmentsForRule(rule, now, GetEndOfDay(now.Date.AddDays(7))),
            _ => []
        };
    }

    public IEnumerable<Appointment> GetAppointmentsForRule(AppointmentRecurrenceRule rule, DateTime rangeStart, DateTime rangeEnd)
    {
        return rule.RecurrenceType.Type switch
        {
            AppointmentRecurrenceType.Daily => GenerateDailyAppointments(rule, rangeStart, rangeEnd),
            AppointmentRecurrenceType.Weekly => GenerateWeeklyAppointments(rule, rangeStart, rangeEnd),
            AppointmentRecurrenceType.Monthly => GenerateMonthlyAppointments(rule, rangeStart, rangeEnd),
            _ => []
        };
    }

    /// <summary>
    ///     Generates daily recurring appointments.
    /// </summary>
    /// <remarks>
    ///     For daily recurrence, appointments are created every day at the same time as the start date,
    ///     starting from the rule's start date until the end date (or one week from now if no end date).
    /// </remarks>
    private static IEnumerable<Appointment> GenerateDailyAppointments(AppointmentRecurrenceRule rule, DateTime rangeStart, DateTime rangeEnd)
    {
        var appointmentDate = rule.StartDate.Date > rangeStart.Date ? rule.StartDate.Date : rangeStart.Date;
        var endDateLimit = GetRangeEndDate(rule, rangeEnd);

        while (appointmentDate <= endDateLimit)
        {
            var appointmentStartTime = appointmentDate.Add(rule.StartDate.TimeOfDay);
            if (appointmentStartTime < rangeStart || appointmentStartTime > rangeEnd)
            {
                appointmentDate = appointmentDate.AddDays(1);
                continue;
            }

            yield return CreateAppointment(rule, appointmentStartTime);

            appointmentDate = appointmentDate.AddDays(1);
        }
    }

    /// <summary>
    ///     Generates weekly recurring appointments.
    /// </summary>
    /// <remarks>
    ///     For weekly recurrence, the <see cref="AppointmentRecurrenceRule.RecurrencePattern" /> contains
    ///     a bitwise value where each power of two represents a day of the week:
    ///     - 1 (2^0) = Monday
    ///     - 2 (2^1) = Tuesday
    ///     - 4 (2^2) = Wednesday
    ///     - 8 (2^3) = Thursday
    ///     - 16 (2^4) = Friday
    ///     - 32 (2^5) = Saturday
    ///     - 64 (2^6) = Sunday
    ///     Appointments are created for each selected day during the current week and next week,
    ///     at the same time as the start date, up to the rule's end date or one week from now.
    /// </remarks>
    private static IEnumerable<Appointment> GenerateWeeklyAppointments(AppointmentRecurrenceRule rule, DateTime rangeStart, DateTime rangeEnd)
    {
        if (rule.RecurrencePattern == null || rule.RecurrencePattern == 0)
        {
            yield break;
        }

        var appointmentDate = rule.StartDate.Date > rangeStart.Date ? rule.StartDate.Date : rangeStart.Date;
        var endDateLimit = GetRangeEndDate(rule, rangeEnd);

        for (;
             appointmentDate <= endDateLimit;
             appointmentDate = appointmentDate.AddDays(1))
        {
            var dayOfWeek = appointmentDate.DayOfWeek;
            var dayFlag = dayOfWeek == DayOfWeek.Sunday ? 64 : 1 << (int)dayOfWeek - 1;

            // Check if this day is enabled in the recurrence pattern
            if ((rule.RecurrencePattern & dayFlag) == 0)
            {
                continue;
            }

            var appointmentStartTime = appointmentDate.Add(rule.StartDate.TimeOfDay);
            if (appointmentStartTime < rangeStart || appointmentStartTime > rangeEnd)
            {
                continue;
            }

            yield return CreateAppointment(rule, appointmentStartTime);
        }
    }

    /// <summary>
    ///     Generates monthly recurring appointments.
    /// </summary>
    /// <remarks>
    ///     For monthly recurrence, the <see cref="AppointmentRecurrenceRule.RecurrencePattern" /> contains
    ///     the day of the month (1-31) on which the appointment should occur.
    ///     Appointments are created for the current and next month on the specified day,
    ///     at the same time as the start date, up to the rule's end date or one week from now.
    ///     If the specified day doesn't exist in a month (e.g., the 31st in February),
    ///     the appointment is skipped for that month.
    /// </remarks>
    private static IEnumerable<Appointment> GenerateMonthlyAppointments(AppointmentRecurrenceRule rule, DateTime rangeStart, DateTime rangeEnd)
    {
        if (rule.RecurrencePattern == null || rule.RecurrencePattern < 1 || rule.RecurrencePattern > 31)
        {
            yield break;
        }

        var dayOfMonth = rule.RecurrencePattern.Value;
        var startDate = rule.StartDate.Date;
        var endDateLimit = GetRangeEndDate(rule, rangeEnd);
        var monthCursor = new DateTime(rangeStart.Year, rangeStart.Month, 1);
        var lastMonth = new DateTime(rangeEnd.Year, rangeEnd.Month, 1);

        while (monthCursor <= lastMonth)
        {
            if (dayOfMonth > DateTime.DaysInMonth(monthCursor.Year, monthCursor.Month))
            {
                monthCursor = monthCursor.AddMonths(1);
                continue;
            }

            var appointmentDate = new DateTime(monthCursor.Year, monthCursor.Month, dayOfMonth);
            if (appointmentDate < startDate || appointmentDate > endDateLimit)
            {
                monthCursor = monthCursor.AddMonths(1);
                continue;
            }

            var appointmentStartTime = appointmentDate.Add(rule.StartDate.TimeOfDay);
            if (appointmentStartTime < rangeStart || appointmentStartTime > rangeEnd)
            {
                monthCursor = monthCursor.AddMonths(1);
                continue;
            }

            yield return CreateAppointment(rule, appointmentStartTime);
            monthCursor = monthCursor.AddMonths(1);
        }
    }

    private static Appointment CreateAppointment(AppointmentRecurrenceRule rule, DateTime appointmentStartTime)
    {
        return new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = rule.Client,
            Service = rule.Service,
            Provider = rule.Provider,
            StartDate = appointmentStartTime,
            EndDate = appointmentStartTime.AddHours(1),
            IsCompleted = false,
            IsCanceled = false,
            RecurringRule = rule
        };
    }

    private static DateTime GetRangeEndDate(AppointmentRecurrenceRule rule, DateTime rangeEnd)
    {
        return rule.EndDate?.Date is { } ruleEndDate && ruleEndDate < rangeEnd.Date
            ? ruleEndDate
            : rangeEnd.Date;
    }

    private static DateTime GetEndOfDay(DateTime date)
    {
        return date.Date.AddDays(1).AddTicks(-1);
    }
}
