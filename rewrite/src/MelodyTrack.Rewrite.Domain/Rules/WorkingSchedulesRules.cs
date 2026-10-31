using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class WorkingScheduleRules
{
    public static Result<WorkingSchedule, ErrorList> Create(WorkingScheduleId id, StaffId staffId, DateOnly effectiveFrom, IEnumerable<WeeklyWorkingSlot> hours, IEnumerable<WeeklyWorkingSlot> breaks, IEnumerable<DatedBreak> datedBreaks)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(staffId.Value, "staffId"));
        if (validation.IsFailure) return validation.Error;
        return new WorkingSchedule(id, staffId, effectiveFrom, hours, breaks, datedBreaks);
    }

    public static Result<WorkingSchedule, ErrorList> Submit(this WorkingSchedule model)
    {
        if (model.Status is not (WorkingScheduleStatus.Draft or WorkingScheduleStatus.Rejected)) return ErrorList.Single(new WorkingScheduleError.InvalidState());
        return model with { Status = WorkingScheduleStatus.Submitted };
    }

    public static Result<WorkingSchedule, ErrorList> Revise(this WorkingSchedule model, DateOnly effectiveFrom, IEnumerable<WeeklyWorkingSlot> hours, IEnumerable<WeeklyWorkingSlot> breaks, IEnumerable<DatedBreak> datedBreaks)
    {
        if (model.Status is not (WorkingScheduleStatus.Draft or WorkingScheduleStatus.Rejected)) return ErrorList.Single(new WorkingScheduleError.InvalidState());
        return model with
        {
            EffectiveFrom = effectiveFrom,
            Hours = hours.ToImmutableArray(),
            Breaks = breaks.ToImmutableArray(),
            DatedBreaks = datedBreaks.ToImmutableArray(),
            Status = WorkingScheduleStatus.Draft
        };
    }

    public static Result<WorkingSchedule, ErrorList> Approve(this WorkingSchedule model, bool hasConflictingAppointments, bool hasConflictingSeries)
    {

        if (model.Status != WorkingScheduleStatus.Submitted) return ErrorList.Single(new WorkingScheduleError.InvalidState());
        var validation = new ErrorListBuilder()
            .AddIf(hasConflictingAppointments, new WorkingScheduleError.ConflictingAppointments())
            .AddIf(hasConflictingSeries, new WorkingScheduleError.ConflictingSeries())
            .ToResult();
        if (validation.IsFailure) return validation.Error;
        return model with { Status = WorkingScheduleStatus.Approved };
    }

    public static Result<WorkingSchedule, ErrorList> Reject(this WorkingSchedule model)
    {

        if (model.Status != WorkingScheduleStatus.Submitted) return ErrorList.Single(new WorkingScheduleError.InvalidState());
        return model with { Status = WorkingScheduleStatus.Rejected };
    }

    public static Result<WorkingPeriods, ErrorList> PeriodsOn(this WorkingSchedule model, DateOnly date)
    {
        if (model.Status != WorkingScheduleStatus.Approved || date < model.EffectiveFrom) return new WorkingPeriods([], []);
        DateOnly[] dates = date > model.EffectiveFrom ? [date.AddDays(-1), date] : [date];
        var hours = new List<TimeInterval>();
        var breaks = new List<TimeInterval>();
        var errors = new ErrorListBuilder();

        void Add(DateOnly day, LocalTimeWindow window, List<TimeInterval> destination)
        {
            var period = window.OnDate(day);
            if (period.IsSuccess) destination.Add(period.Value);
            else errors.AddRange(period.Error.Issues.Select(issue => new DomainIssue(new WorkingScheduleError.InvalidTime((TimeError)issue.Error))));
        }

        foreach (var day in dates)
        {
            foreach (var slot in model.Hours.Where(slot => slot.Weekday == Weekdays.OfDate(day))) Add(day, slot.Window, hours);
            foreach (var slot in model.Breaks.Where(slot => slot.Weekday == Weekdays.OfDate(day))) Add(day, slot.Window, breaks);
            foreach (var slot in model.DatedBreaks.Where(slot => slot.Date == day)) Add(day, slot.Window, breaks);
        }
        var validation = errors.ToResult();
        if (validation.IsFailure) return validation.Error;
        return new WorkingPeriods(hours.ToImmutableArray(), breaks.ToImmutableArray());
    }
}
