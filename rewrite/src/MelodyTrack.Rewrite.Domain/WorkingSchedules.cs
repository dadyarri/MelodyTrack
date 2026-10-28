using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Scheduling;

public sealed record LocalTimeWindow
{
    private LocalTimeWindow(TimeOnly startsAt, TimeOnly endsAt) { StartsAt = startsAt; EndsAt = endsAt; }
    public TimeOnly StartsAt { get; }
    public TimeOnly EndsAt { get; }

    public static Result<LocalTimeWindow, ErrorList> Create(TimeOnly startsAt, TimeOnly endsAt) => startsAt == endsAt
        ? ErrorList.Of(new TimeError.EmptyOrReversedInterval()) : new LocalTimeWindow(startsAt, endsAt);

    public Result<TimeInterval, ErrorList> OnDate(DateOnly date)
    {
        var crossesMidnight = EndsAt < StartsAt;
        if (crossesMidnight && date == DateOnly.MaxValue) return ErrorList.Of(new TimeError.OutOfRange());
        var endDate = crossesMidnight ? date.AddDays(1) : date;
        var startsAt = SchoolTime.At(date, StartsAt);
        var endsAt = SchoolTime.At(endDate, EndsAt);
        var validation = Validation.Collect(Validation.Check(startsAt), Validation.Check(endsAt));
        if (validation.IsFailure) return validation.Error;
        return TimeInterval.Create(startsAt.Value, endsAt.Value);
    }
}

public sealed record WeeklyWorkingSlot(Weekday Weekday, LocalTimeWindow Window);
public sealed record DatedBreak(DateOnly Date, LocalTimeWindow Window);
public enum WorkingScheduleStatus { Draft, Submitted, Approved, Rejected }
public sealed record WorkingPeriods(ImmutableArray<TimeInterval> Hours, ImmutableArray<TimeInterval> Breaks);

public sealed record WorkingSchedule
{
    private WorkingSchedule(WorkingScheduleId id, StaffId staffId, DateOnly effectiveFrom, IEnumerable<WeeklyWorkingSlot> hours,
        IEnumerable<WeeklyWorkingSlot> breaks, IEnumerable<DatedBreak> datedBreaks)
    {
        Id = id; StaffId = staffId; EffectiveFrom = effectiveFrom;
        Hours = hours.ToImmutableArray(); Breaks = breaks.ToImmutableArray(); DatedBreaks = datedBreaks.ToImmutableArray();
    }
    public WorkingScheduleId Id { get; }
    public StaffId StaffId { get; }
    public DateOnly EffectiveFrom { get; private init; }
    public ImmutableArray<WeeklyWorkingSlot> Hours { get; private init; }
    public ImmutableArray<WeeklyWorkingSlot> Breaks { get; private init; }
    public ImmutableArray<DatedBreak> DatedBreaks { get; private init; }
    public WorkingScheduleStatus Status { get; private init; } = WorkingScheduleStatus.Draft;

    public static Result<WorkingSchedule, ErrorList> Create(WorkingScheduleId id, StaffId staffId, DateOnly effectiveFrom,
        IEnumerable<WeeklyWorkingSlot> hours, IEnumerable<WeeklyWorkingSlot> breaks, IEnumerable<DatedBreak> datedBreaks)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(staffId.Value, "staffId"));
        if (validation.IsFailure) return validation.Error;
        return new WorkingSchedule(id, staffId, effectiveFrom, hours, breaks, datedBreaks);
    }

    public Result<WorkingSchedule, ErrorList> Submit()
    {
        if (Status is not (WorkingScheduleStatus.Draft or WorkingScheduleStatus.Rejected)) return ErrorList.Of(new WorkingScheduleError.InvalidState());
        return this with { Status = WorkingScheduleStatus.Submitted };
    }

    public Result<WorkingSchedule, ErrorList> Revise(DateOnly effectiveFrom, IEnumerable<WeeklyWorkingSlot> hours,
        IEnumerable<WeeklyWorkingSlot> breaks, IEnumerable<DatedBreak> datedBreaks)
    {
        if (Status is not (WorkingScheduleStatus.Draft or WorkingScheduleStatus.Rejected)) return ErrorList.Of(new WorkingScheduleError.InvalidState());
        return this with { EffectiveFrom = effectiveFrom, Hours = hours.ToImmutableArray(), Breaks = breaks.ToImmutableArray(),
            DatedBreaks = datedBreaks.ToImmutableArray(), Status = WorkingScheduleStatus.Draft };
    }

    public Result<WorkingSchedule, ErrorList> Approve(StaffRole role, bool hasConflictingAppointments, bool hasConflictingSeries)
    {
        if (role != StaffRole.Superuser) return ErrorList.Of(new WorkingScheduleError.Forbidden());
        if (Status != WorkingScheduleStatus.Submitted) return ErrorList.Of(new WorkingScheduleError.InvalidState());
        var errors = new List<DomainIssue>();
        if (hasConflictingAppointments) errors.Add(new(new WorkingScheduleError.ConflictingAppointments()));
        if (hasConflictingSeries) errors.Add(new(new WorkingScheduleError.ConflictingSeries()));
        if (errors.Count > 0) return ErrorList.From(errors);
        return this with { Status = WorkingScheduleStatus.Approved };
    }

    public Result<WorkingSchedule, ErrorList> Reject(StaffRole role)
    {
        if (role != StaffRole.Superuser) return ErrorList.Of(new WorkingScheduleError.Forbidden());
        if (Status != WorkingScheduleStatus.Submitted) return ErrorList.Of(new WorkingScheduleError.InvalidState());
        return this with { Status = WorkingScheduleStatus.Rejected };
    }

    public Result<WorkingPeriods, ErrorList> PeriodsOn(DateOnly date)
    {
        if (Status != WorkingScheduleStatus.Approved || date < EffectiveFrom) return new WorkingPeriods([], []);
        DateOnly[] dates = date > EffectiveFrom ? [date.AddDays(-1), date] : [date];
        var hours = new List<TimeInterval>();
        var breaks = new List<TimeInterval>();
        var errors = new List<DomainIssue>();

        void Add(DateOnly day, LocalTimeWindow window, List<TimeInterval> destination)
        {
            var period = window.OnDate(day);
            if (period.IsSuccess) destination.Add(period.Value);
            else errors.AddRange(period.Error.Issues.Select(issue => new DomainIssue(new WorkingScheduleError.InvalidTime((TimeError)issue.Error))));
        }

        foreach (var day in dates)
        {
            foreach (var slot in Hours.Where(slot => slot.Weekday == Weekdays.OfDate(day))) Add(day, slot.Window, hours);
            foreach (var slot in Breaks.Where(slot => slot.Weekday == Weekdays.OfDate(day))) Add(day, slot.Window, breaks);
            foreach (var slot in DatedBreaks.Where(slot => slot.Date == day)) Add(day, slot.Window, breaks);
        }
        if (errors.Count > 0) return ErrorList.From(errors);
        return new WorkingPeriods(hours.ToImmutableArray(), breaks.ToImmutableArray());
    }
}
