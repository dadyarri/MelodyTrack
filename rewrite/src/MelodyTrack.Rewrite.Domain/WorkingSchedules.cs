using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public sealed record LocalTimeWindow
{
    private LocalTimeWindow(TimeOnly startsAt, TimeOnly endsAt) { StartsAt = startsAt; EndsAt = endsAt; }
    public TimeOnly StartsAt { get; }
    public TimeOnly EndsAt { get; }

    public static Result<LocalTimeWindow, ErrorList> Create(TimeOnly startsAt, TimeOnly endsAt) => startsAt == endsAt
        ? ErrorList.Single(new TimeError.EmptyOrReversedInterval()) : new LocalTimeWindow(startsAt, endsAt);

    public Result<TimeInterval, ErrorList> OnDate(DateOnly date)
    {
        var crossesMidnight = EndsAt < StartsAt;
        if (crossesMidnight && date == DateOnly.MaxValue) return ErrorList.Single(new TimeError.OutOfRange());
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
    internal WorkingSchedule(WorkingScheduleId id, StaffId staffId, DateOnly effectiveFrom, IEnumerable<WeeklyWorkingSlot> hours,
        IEnumerable<WeeklyWorkingSlot> breaks, IEnumerable<DatedBreak> datedBreaks)
    {
        Id = id; StaffId = staffId; EffectiveFrom = effectiveFrom;
        Hours = hours.ToImmutableArray(); Breaks = breaks.ToImmutableArray(); DatedBreaks = datedBreaks.ToImmutableArray();
    }

    public WorkingScheduleId Id { get; }

    public StaffId StaffId { get; }

    public DateOnly EffectiveFrom { get; internal init; }

    public ImmutableArray<WeeklyWorkingSlot> Hours { get; internal init; }

    public ImmutableArray<WeeklyWorkingSlot> Breaks { get; internal init; }

    public ImmutableArray<DatedBreak> DatedBreaks { get; internal init; }

    public WorkingScheduleStatus Status { get; internal init; } = WorkingScheduleStatus.Draft;
}
