using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public enum AppointmentDeleteScope
{
    Single,
    ThisAndFollowing,
    All,
    WeekdayThisAndFollowing,
    WeekdayAll
}

public enum DeleteAppointmentResult
{
    Success,
    NotFound
}

public interface IAppointmentDeletionService
{
    Task<DeleteAppointmentResult> DeleteAsync(Ulid appointmentId, AppointmentDeleteScope scope, CancellationToken ct);
}

public class AppointmentDeletionService(AppDbContext db) : IAppointmentDeletionService
{
    public async Task<DeleteAppointmentResult> DeleteAsync(Ulid appointmentId, AppointmentDeleteScope scope, CancellationToken ct)
    {
        var appointment = await db.Appointments
            .Include(item => item.Client)
            .Include(item => item.Service)
            .Include(item => item.Provider)
            .Include(item => item.RecurringRule)
            .ThenInclude(item => item!.RecurrenceType)
            .FirstOrDefaultAsync(item => item.Id == appointmentId && !item.IsDeleted, ct);

        if (appointment is null)
        {
            return DeleteAppointmentResult.NotFound;
        }

        if (appointment.RecurringRule is null || scope == AppointmentDeleteScope.Single)
        {
            appointment.IsDeleted = true;
            await db.SaveChangesAsync(ct);
            return DeleteAppointmentResult.Success;
        }

        if (scope is AppointmentDeleteScope.WeekdayThisAndFollowing or AppointmentDeleteScope.WeekdayAll)
        {
            var normalizedScope = NormalizeWeekdayScope(appointment.RecurringRule, scope);
            if (normalizedScope != scope)
            {
                scope = normalizedScope;
            }
            else
            {
                await using var weekdayTransaction = await db.Database.BeginTransactionAsync(ct);
                await DeleteWeeklyDayScopeAsync(appointment, scope, ct);
                await db.SaveChangesAsync(ct);
                await weekdayTransaction.CommitAsync(ct);
                return DeleteAppointmentResult.Success;
            }
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var recurringRuleId = appointment.RecurringRule.Id;
        var recurringRule = appointment.RecurringRule;

        if (scope == AppointmentDeleteScope.ThisAndFollowing)
        {
            await db.Appointments
                .Where(item =>
                    item.RecurringRule != null &&
                    item.RecurringRule.Id == recurringRuleId &&
                    item.StartDate >= appointment.StartDate &&
                    !item.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.IsDeleted, true), ct);

            if (appointment.StartDate.Date <= appointment.RecurringRule.StartDate.Date)
            {
                await DetachAppointmentsFromRecurringRuleAsync(recurringRuleId, ct);
                db.RecurrenceRules.Remove(recurringRule);
            }
            else
            {
                appointment.RecurringRule.EndDate = appointment.StartDate.Date.AddDays(-1);
            }
        }
        else
        {
            await db.Appointments
                .Where(item =>
                    item.RecurringRule != null &&
                    item.RecurringRule.Id == recurringRuleId &&
                    !item.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.IsDeleted, true), ct);

            await DetachAppointmentsFromRecurringRuleAsync(recurringRuleId, ct);
            db.RecurrenceRules.Remove(recurringRule);
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return DeleteAppointmentResult.Success;
    }

    private async Task DeleteWeeklyDayScopeAsync(Appointment appointment, AppointmentDeleteScope scope, CancellationToken ct)
    {
        var recurringRule = appointment.RecurringRule!;
        var recurringRuleId = recurringRule.Id;
        var dayFlag = GetDayFlag(appointment.StartDate.DayOfWeek);
        var remainingPattern = (recurringRule.RecurrencePattern ?? 0) & ~dayFlag;

        if (scope == AppointmentDeleteScope.WeekdayThisAndFollowing)
        {
            if (HasEarlierWeekdayOccurrence(recurringRule.StartDate, appointment.StartDate, appointment.StartDate.DayOfWeek))
            {
                var historicalRule = new AppointmentRecurrenceRule
                {
                    Id = Ulid.NewUlid(),
                    Client = appointment.Client,
                    Service = appointment.Service,
                    Provider = appointment.Provider,
                    StartDate = recurringRule.StartDate,
                    EndDate = appointment.StartDate.Date.AddDays(-1),
                    RecurrenceType = recurringRule.RecurrenceType,
                    RecurrencePattern = dayFlag
                };

                await db.RecurrenceRules.AddAsync(historicalRule, ct);

                var earlierWeekdayAppointments = await db.Appointments
                    .Where(item =>
                        item.RecurringRule != null &&
                        item.RecurringRule.Id == recurringRuleId &&
                        item.StartDate < appointment.StartDate &&
                        item.StartDate.DayOfWeek == appointment.StartDate.DayOfWeek)
                    .ToListAsync(ct);

                foreach (var earlierAppointment in earlierWeekdayAppointments)
                {
                    earlierAppointment.RecurringRule = historicalRule;
                }
            }

            await db.Appointments
                .Where(item =>
                    item.RecurringRule != null &&
                    item.RecurringRule.Id == recurringRuleId &&
                    item.StartDate >= appointment.StartDate &&
                    item.StartDate.DayOfWeek == appointment.StartDate.DayOfWeek &&
                    !item.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.IsDeleted, true), ct);
        }
        else
        {
            await db.Appointments
                .Where(item =>
                    item.RecurringRule != null &&
                    item.RecurringRule.Id == recurringRuleId &&
                    item.StartDate.DayOfWeek == appointment.StartDate.DayOfWeek &&
                    !item.IsDeleted)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(item => item.IsDeleted, true), ct);
        }

        recurringRule.RecurrencePattern = remainingPattern;
    }

    private async Task DetachAppointmentsFromRecurringRuleAsync(Ulid recurringRuleId, CancellationToken ct)
    {
        var appointments = await db.Appointments
            .Include(item => item.RecurringRule)
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == recurringRuleId)
            .ToListAsync(ct);

        foreach (var item in appointments)
        {
            item.RecurringRule = null;
        }
    }

    private static AppointmentDeleteScope NormalizeWeekdayScope(AppointmentRecurrenceRule recurringRule, AppointmentDeleteScope scope)
    {
        if (recurringRule.RecurrenceType.Type != AppointmentRecurrenceType.Weekly || CountEnabledDays(recurringRule.RecurrencePattern) <= 1)
        {
            return scope == AppointmentDeleteScope.WeekdayAll
                ? AppointmentDeleteScope.All
                : AppointmentDeleteScope.ThisAndFollowing;
        }

        return scope;
    }

    private static int CountEnabledDays(int? recurrencePattern)
    {
        if (recurrencePattern is null or 0)
        {
            return 0;
        }

        var count = 0;
        for (var bit = 0; bit < 7; bit++)
        {
            if ((recurrencePattern.Value & (1 << bit)) != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static bool HasEarlierWeekdayOccurrence(DateTime ruleStartDate, DateTime appointmentStartDate, DayOfWeek dayOfWeek)
    {
        var firstOccurrenceDate = GetFirstOccurrenceOnOrAfter(ruleStartDate.Date, dayOfWeek);
        return firstOccurrenceDate < appointmentStartDate.Date;
    }

    private static DateTime GetFirstOccurrenceOnOrAfter(DateTime startDate, DayOfWeek dayOfWeek)
    {
        var offset = ((int)dayOfWeek - (int)startDate.DayOfWeek + 7) % 7;
        return startDate.AddDays(offset);
    }

    private static int GetDayFlag(DayOfWeek dayOfWeek)
    {
        return dayOfWeek == DayOfWeek.Sunday
            ? 64
            : 1 << ((int)dayOfWeek - 1);
    }
}
