using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using MelodyTrack.Core.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public sealed class RecurringAppointmentReschedulingService(
    AppDbContext db,
    IRecurringAppointmentService recurrence,
    ISchedulingAvailabilityService availability,
    TimeProvider timeProvider)
{
    public async Task<string?> RescheduleAsync(Appointment selected, DateTime nextStartUtc, bool all, CancellationToken ct)
    {
        await VacationRequestSubjectLock.AcquireSchedulingAsync(db, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (selected.StartDate <= now || nextStartUtc <= now)
            return "Перенос серии доступен только для будущих занятий.";
        var original = selected.RecurringRule!;
        var first = all ? recurrence.GetAppointmentsForRule(original, now, selected.StartDate).FirstOrDefault() : selected;
        if (first is null) return "Не найдено начало будущей части серии.";
        var cutoff = first.StartDate;
        var ordinalOffset = recurrence.GetOrdinal(original, cutoff);
        var localSelected = DateTimeUtils.ConvertDateToTimezone(selected.StartDate, original.Timezone);
        var nextTimezone = selected.Provider?.ScheduleTimezone ?? original.Timezone;
        var localNext = DateTimeUtils.ConvertDateToTimezone(nextStartUtc, nextTimezone);
        var localDelta = localNext - localSelected;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(nextTimezone);
        DateTime? Shift(DateTime utc)
        {
            var local = DateTime.SpecifyKind(DateTimeUtils.ConvertDateToTimezone(utc, original.Timezone).Add(localDelta), DateTimeKind.Unspecified);
            var instants = SchedulingAvailability.GetUtcInstants(local, zone);
            return instants.Count == 0 ? null : instants[^1];
        }
        var shiftedCutoff = Shift(cutoff);
        if (shiftedCutoff is null) return "Начало серии попадает в пропущенный час при смене часового пояса.";
        if (shiftedCutoff <= now) return "Начало будущей части серии нельзя перенести в прошлое.";
        if (original.EndDate is { } originalEnd && Shift(originalEnd) is null) return "Окончание серии попадает в недопустимый час.";
        var dayOffset = (localNext.Date - localSelected.Date).Days;
        var next = new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(), Client = original.Client, Service = original.Service, Provider = original.Provider,
            Timezone = nextTimezone, DurationMinutes = original.DurationMinutes,
            StartDate = shiftedCutoff.Value, EndDate = original.EndDate is { } end ? Shift(end) : null,
            RecurrenceType = original.RecurrenceType,
            RecurrencePattern = original.RecurrenceType.Type switch
            {
                AppointmentRecurrenceType.Weekly => RotateWeek(original.RecurrencePattern ?? 0, dayOffset),
                AppointmentRecurrenceType.Monthly => localNext.Day,
                _ => original.RecurrencePattern
            }
        };
        var stored = await db.Appointments.Where(item => item.RecurringRuleId == original.Id && item.StartDate >= cutoff)
            .Include(item => item.Client).Include(item => item.Provider).Include(item => item.Service).ToListAsync(ct);
        var movable = stored.Where(item => item.Status is AppointmentStatus.Planned or AppointmentStatus.Cancelled || item.IsDeleted).ToList();
        var moved = movable.Select(item => (Item: item, Occurrence: recurrence.GetOccurrence(next,
            (item.RecurrenceOrdinal ?? recurrence.GetOrdinal(original, item.StartDate)) - ordinalOffset))).ToList();
        if (moved.Any(item => item.Occurrence is null || item.Occurrence.StartDate <= now))
            return "Некоторые занятия попадают в прошлое или недопустимую дату. Выберите другой перенос.";
        var excludedIds = movable.Select(item => item.Id).ToArray();
        var planned = moved.Where(item => !item.Item.IsDeleted && item.Item.Status == AppointmentStatus.Planned).ToList();
        foreach (var (item, occurrence) in planned)
        {
            var blockers = await availability.EvaluateAsync(item.Client.Id, item.Provider?.Id,
                occurrence!.StartDate, occurrence.EndDate, excludedIds, ct, [original.Id]);
            if (blockers.Count > 0) return $"Перенос серии невозможен: {string.Join(" ", blockers.Select(blocker => blocker.Message).Distinct())}";
        }
        for (var index = 0; index < planned.Count; index++)
        {
            if (planned.Skip(index + 1).Any(other => SchedulingAvailability.Overlaps(planned[index].Occurrence!.StartDate,
                planned[index].Occurrence!.EndDate, other.Occurrence!.StartDate, other.Occurrence.EndDate)))
                return "После переноса занятия серии пересекаются между собой.";
        }
        var interruptions = await db.SchedulingInterruptions.Where(item => item.RecurringRuleId == original.Id
            && item.StartDate >= cutoff).ToListAsync(ct);
        original.EndDate = cutoff.AddTicks(-10);
        db.RecurrenceRules.Add(next);
        foreach (var preserved in stored.Except(movable))
        {
            var ordinal = (preserved.RecurrenceOrdinal ?? recurrence.GetOrdinal(original, preserved.StartDate)) - ordinalOffset;
            var occurrence = recurrence.GetOccurrence(next, ordinal);
            if (occurrence is null) continue;
            db.SchedulingInterruptions.Add(new SchedulingInterruption
            {
                Id = Ulid.NewUlid(), SubjectType = VacationRequestSubjectType.Client, SubjectId = preserved.Client.Id,
                RecurringRule = next, RecurringRuleId = next.Id, RecurrenceOrdinal = ordinal,
                StartDate = occurrence.StartDate, EndDate = occurrence.EndDate, Reason = "preserved_history"
            });
        }
        foreach (var (item, occurrence) in moved)
        {
            item.RecurringRule = next;
            item.RecurringRuleId = next.Id;
            item.RecurrenceOrdinal = occurrence!.RecurrenceOrdinal;
            item.StartDate = occurrence.StartDate;
            item.EndDate = occurrence.EndDate;
        }
        foreach (var interruption in interruptions)
        {
            var ordinal = (interruption.RecurrenceOrdinal ?? recurrence.GetOrdinal(original, interruption.StartDate)) - ordinalOffset;
            var occurrence = recurrence.GetOccurrence(next, ordinal);
            if (occurrence is null) continue;
            interruption.RecurringRule = next;
            interruption.RecurringRuleId = next.Id;
            interruption.RecurrenceOrdinal = ordinal;
            interruption.StartDate = occurrence.StartDate;
            interruption.EndDate = occurrence.EndDate;
        }
        await db.SaveChangesAsync(ct);
        return null;
    }

    private static int RotateWeek(int pattern, int offset)
    {
        var shift = (offset % 7 + 7) % 7;
        return ((pattern << shift) | (pattern >> (7 - shift))) & 127;
    }
}
