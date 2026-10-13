using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
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

public enum DeleteAppointmentResult { Success, NotFound }

public interface IAppointmentDeletionService
{
    Task<DeleteAppointmentResult> DeleteAsync(Ulid appointmentId, AppointmentDeleteScope scope, CancellationToken ct);
}

public sealed class AppointmentDeletionService(AppDbContext db, IRecurringAppointmentService recurrence, TimeProvider timeProvider)
    : IAppointmentDeletionService
{
    public async Task<DeleteAppointmentResult> DeleteAsync(Ulid appointmentId, AppointmentDeleteScope scope, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await VacationRequestSubjectLock.AcquireSchedulingAsync(db, ct);
        var selected = await db.Appointments.Include(item => item.Client).Include(item => item.Service).Include(item => item.Provider)
            .Include(item => item.RecurringRule).ThenInclude(item => item!.RecurrenceType)
            .SingleOrDefaultAsync(item => item.Id == appointmentId && !item.IsDeleted, ct);
        if (selected is null) return DeleteAppointmentResult.NotFound;
        if (selected.RecurringRule is null || scope == AppointmentDeleteScope.Single)
        {
            selected.IsDeleted = true;
        }
        else
        {
            var rule = selected.RecurringRule;
            var now = timeProvider.GetUtcNow().UtcDateTime;
            var cutoff = scope is AppointmentDeleteScope.All or AppointmentDeleteScope.WeekdayAll
                ? now : selected.StartDate > now ? selected.StartDate : now;
            var weekdayOnly = scope is AppointmentDeleteScope.WeekdayAll or AppointmentDeleteScope.WeekdayThisAndFollowing
                && rule.RecurrenceType.Type == AppointmentRecurrenceType.Weekly;
            var weekday = DateTimeUtils.ConvertDateToTimezone(selected.StartDate, rule.Timezone).DayOfWeek;
            var pattern = (rule.RecurrencePattern ?? 0) & ~RecurringAppointmentService.DayFlag(weekday);
            var stored = await db.Appointments.Where(item => item.RecurringRuleId == rule.Id && item.StartDate >= cutoff).ToListAsync(ct);
            var interruptions = await db.SchedulingInterruptions.Where(item => item.RecurringRuleId == rule.Id && item.StartDate >= cutoff).ToListAsync(ct);
            AppointmentRecurrenceRule? next = null;
            if (weekdayOnly && pattern != 0)
            {
                var searchStart = cutoff > rule.StartDate ? cutoff : rule.StartDate;
                var first = recurrence.GetAppointmentsForRule(rule, searchStart, searchStart.AddDays(7)).FirstOrDefault();
                if (first is not null)
                {
                    next = new AppointmentRecurrenceRule
                    {
                        Id = Ulid.NewUlid(), Client = rule.Client, Service = rule.Service, Provider = rule.Provider,
                        StartDate = first.StartDate, EndDate = rule.EndDate, Timezone = rule.Timezone,
                        DurationMinutes = rule.DurationMinutes, RecurrenceType = rule.RecurrenceType, RecurrencePattern = pattern
                    };
                    db.RecurrenceRules.Add(next);
                }
            }
            rule.EndDate = cutoff.AddTicks(-10);
            foreach (var item in stored)
            {
                if (item.Status is AppointmentStatus.Completed or AppointmentStatus.Burned && !item.IsDeleted) continue;
                var removed = next is null || DateTimeUtils.ConvertDateToTimezone(item.StartDate, rule.Timezone).DayOfWeek == weekday;
                if (removed) item.IsDeleted = true;
                else
                {
                    item.RecurringRule = next;
                    item.RecurringRuleId = next!.Id;
                    item.RecurrenceOrdinal = recurrence.GetOrdinal(next, item.StartDate);
                }
            }
            foreach (var item in interruptions)
            {
                if (next is null || DateTimeUtils.ConvertDateToTimezone(item.StartDate, rule.Timezone).DayOfWeek == weekday) continue;
                item.RecurringRule = next;
                item.RecurringRuleId = next.Id;
                item.RecurrenceOrdinal = recurrence.GetOrdinal(next, item.StartDate);
            }
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return DeleteAppointmentResult.Success;
    }
}
