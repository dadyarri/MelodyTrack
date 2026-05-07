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
}

/// <inheritdoc />
public class RecurringAppointmentService : IRecurringAppointmentService
{
    /// <inheritdoc />
    public IEnumerable<Appointment> GetAppointmentsForRule(AppointmentRecurrenceRule rule, DateTime now)
    {
        return rule.RecurrenceType.Type switch
        {
            AppointmentRecurrenceType.Daily => GenerateDailyAppointments(rule, now),
            AppointmentRecurrenceType.Weekly => GenerateWeeklyAppointments(rule, now),
            AppointmentRecurrenceType.Monthly => GenerateMonthlyAppointments(rule, now),
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
    private static IEnumerable<Appointment> GenerateDailyAppointments(AppointmentRecurrenceRule rule, DateTime now)
    {
        var startDate = rule.StartDate.Date;

        // Determine the start date for appointment generation (today or later)
        var appointmentDate = now.Date > startDate ? now.Date : startDate;

        // Determine the end date for the period we're generating appointments for (rule end date or now + 1 week)
        var endDateLimit = rule.EndDate?.Date ?? now.Date.AddDays(6);

        while (appointmentDate <= endDateLimit)
        {
            var appointmentStartTime = appointmentDate.Add(rule.StartDate.TimeOfDay);

            yield return new Appointment
            {
                Client = rule.Client,
                Service = rule.Service,
                Provider = rule.Provider,
                StartDate = appointmentStartTime,
                EndDate = appointmentStartTime.AddHours(1),
                IsCompleted = false,
                IsCanceled = false,
                RecurringRule = rule
            };

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
    private static IEnumerable<Appointment> GenerateWeeklyAppointments(AppointmentRecurrenceRule rule, DateTime now)
    {
        if (rule.RecurrencePattern == null || rule.RecurrencePattern == 0)
        {
            yield break;
        }

        var endDateLimit = rule.EndDate?.Date ?? now.Date.AddDays(7);

        // Check for appointments in the current week and next week
        var currentDate = now.Date;
        var weekEndDate = currentDate.AddDays(7);

        for (var appointmentDate = currentDate;
             appointmentDate <= weekEndDate && appointmentDate <= endDateLimit;
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

            yield return new Appointment
            {
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
    private static IEnumerable<Appointment> GenerateMonthlyAppointments(AppointmentRecurrenceRule rule, DateTime now)
    {
        if (rule.RecurrencePattern == null || rule.RecurrencePattern < 1 || rule.RecurrencePattern > 31)
        {
            yield break;
        }

        var dayOfMonth = rule.RecurrencePattern.Value;
        var startDate = rule.StartDate.Date;
        var endDateLimit = rule.EndDate?.Date ?? now.Date.AddDays(7);

        // Check for appointments in the current and next month
        var currentMonth = new DateTime(now.Year, now.Month, 1);
        var nextMonth = currentMonth.AddMonths(1);

        foreach (var monthStart in new[] { currentMonth, nextMonth })
        {
            // Attempt to create a date for the specified day of the month
            if (dayOfMonth > DateTime.DaysInMonth(monthStart.Year, monthStart.Month))
            {
                continue; // Skip if the day doesn't exist in this month
            }

            var appointmentDate = new DateTime(monthStart.Year, monthStart.Month, dayOfMonth);

            // Skip if the appointment date is before the rule's start date or after the rule's end date
            if (appointmentDate < startDate || appointmentDate > endDateLimit)
            {
                continue;
            }

            // Skip if the appointment date is before today
            if (appointmentDate < now.Date)
            {
                continue;
            }

            var appointmentStartTime = appointmentDate.Add(rule.StartDate.TimeOfDay);

            yield return new Appointment
            {
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
    }
}