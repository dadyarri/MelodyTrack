using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public enum Weekday { Monday, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday }

public static class Weekdays
{
    public static Weekday OfDate(DateOnly date) => (Weekday)(((int)date.DayOfWeek + 6) % 7);
}

public sealed record SequenceNumber : IComparable<SequenceNumber>
{
    private SequenceNumber(long value) => Value = value;
    public long Value { get; }
    public static SequenceNumber First { get; } = new(1);
    public static Result<SequenceNumber, ErrorList> Create(long value) => value <= 0
        ? ErrorList.Of(new SchedulingError.InvalidSequenceNumber(value)) : new SequenceNumber(value);
    public Result<SequenceNumber, ErrorList> Next() => Value == long.MaxValue
        ? ErrorList.Of(new SchedulingError.SequenceNumberExhausted()) : new SequenceNumber(Value + 1);
    public int CompareTo(SequenceNumber? other) => other is null ? 1 : Value.CompareTo(other.Value);
}

public sealed record LessonDuration
{
    private LessonDuration(int minutes) => Minutes = minutes;
    public int Minutes { get; }
    public TimeSpan ToTimeSpan() => TimeSpan.FromMinutes(Minutes);
    public static Result<LessonDuration, ErrorList> Create(int minutes) => minutes is 30 or 45 or 60 or 90 or 120
        ? new LessonDuration(minutes) : ErrorList.Of(new SchedulingError.InvalidLessonDuration(minutes));
}

public sealed record StartHour
{
    private StartHour(int value) => Value = value;
    public int Value { get; }
    public TimeOnly ToTimeOnly() => new(Value, 0);
    public static Result<StartHour, ErrorList> Create(int value) => value is >= 0 and <= 23
        ? new StartHour(value) : ErrorList.Of(new SchedulingError.InvalidStartHour(value));
}

public sealed record AppointmentCount
{
    private AppointmentCount(int value) => Value = value;
    public int Value { get; }
    public static Result<AppointmentCount, ErrorList> Create(int value) => value > 0
        ? new AppointmentCount(value) : ErrorList.Of(new SchedulingError.InvalidAppointmentCount(value));
}

public sealed record WeeklySlot(Weekday Weekday, StartHour StartHour);
public sealed record WeeklyOccurrence(DateOnly Date, StartHour StartHour);

public sealed record WeeklyRule
{
    private WeeklyRule(ImmutableArray<WeeklySlot> slots) => Slots = slots;
    public ImmutableArray<WeeklySlot> Slots { get; }

    public static Result<WeeklyRule, ErrorList> Create(IEnumerable<WeeklySlot> slots)
    {
        var values = slots.OrderBy(slot => slot.Weekday).ToImmutableArray();
        if (values.IsEmpty) return ErrorList.Of(new SchedulingError.EmptyWeeklyRule());
        var errors = values.GroupBy(slot => slot.Weekday).Where(group => group.Count() > 1)
            .Select(group => new DomainIssue(new SchedulingError.DuplicateWeekday(group.Key))).ToArray();
        if (errors.Length > 0) return ErrorList.From(errors);
        return new WeeklyRule(values);
    }

    public WeeklyOccurrence? NextOnOrAfter(DateOnly date)
    {
        var weekday = Weekdays.OfDate(date);
        var next = Slots.Select(slot => (Slot: slot, Distance: ((int)slot.Weekday - (int)weekday + 7) % 7)).MinBy(value => value.Distance);
        return date.DayNumber > DateOnly.MaxValue.DayNumber - next.Distance ? null : new(date.AddDays(next.Distance), next.Slot.StartHour);
    }
}

public closed record SeriesEnd
{
    public sealed record OnDateInclusive(DateOnly Date) : SeriesEnd;
    public sealed record AfterAppointments(AppointmentCount Count) : SeriesEnd;
    public sealed record OpenEnded : SeriesEnd;
}

public sealed record SeriesRule(WeeklyRule Weekly, StaffId StaffId, RoomId? RoomId);
public sealed record SeriesCandidate(SequenceNumber SequenceNumber, DateTimeOffset StartsAt);
public sealed record SeriesStep(SeriesCandidate Candidate, Series Series);

public sealed record Series
{
    private Series(SeriesId id, ClientId clientId, ServiceId serviceId, LessonDuration duration, DateOnly startsOn, SeriesEnd end, SeriesRule rule)
    {
        Id = id; ClientId = clientId; ServiceId = serviceId; Duration = duration; StartsOn = startsOn; End = end; Rule = rule; NextDate = startsOn;
    }

    public SeriesId Id { get; }
    public ClientId ClientId { get; }
    public ServiceId ServiceId { get; }
    public LessonDuration Duration { get; }
    public DateOnly StartsOn { get; }
    public SeriesEnd End { get; }
    public SeriesRule Rule { get; private init; }
    public SequenceNumber NextSequenceNumber { get; private init; } = SequenceNumber.First;
    public DateOnly? NextDate { get; private init; }
    public DateOnly? TerminatedFrom { get; private init; }

    public static Result<Series, ErrorList> Create(SeriesId id, ClientId clientId, ServiceId serviceId, LessonDuration duration, DateOnly startsOn, SeriesEnd end, SeriesRule rule)
    {
        var ids = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"),
            Validation.Identifier(serviceId.Value, "serviceId"), Validation.Identifier(rule.StaffId.Value, "staffId"),
            rule.RoomId is { } room ? Validation.Identifier(room.Value, "roomId") : UnitResult.Success<ErrorList>());
        if (ids.IsFailure) return ids.Error;
        if (end is SeriesEnd.OnDateInclusive ending && ending.Date < startsOn)
            return ErrorList.Of(new SchedulingError.SeriesEndBeforeStart(startsOn, ending.Date));
        return new Series(id, clientId, serviceId, duration, startsOn, end, rule);
    }

    public Result<Series, ErrorList> Finish(DateOnly fromDate)
    {
        if (TerminatedFrom is { } previous && previous != fromDate) return ErrorList.Of(new SchedulingError.SeriesFinished());
        return this with { TerminatedFrom = fromDate };
    }

    public Result<Series, ErrorList> ChangeRule(SeriesRule rule)
    {
        if (TerminatedFrom.HasValue) return ErrorList.Of(new SchedulingError.SeriesFinished());
        var ids = Validation.Collect(Validation.Identifier(rule.StaffId.Value, "staffId"),
            rule.RoomId is { } room ? Validation.Identifier(room.Value, "roomId") : UnitResult.Success<ErrorList>());
        if (ids.IsFailure) return ids.Error;
        return this with { Rule = rule };
    }

    public Result<SeriesStep?, ErrorList> NextCandidate(int countedAppointments)
    {
        if (countedAppointments < 0) return ErrorList.Of(new SchedulingError.InvalidCountedAppointments());
        var next = NextDate is { } date ? Rule.Weekly.NextOnOrAfter(date) : null;
        if (next is null || End is SeriesEnd.AfterAppointments count && countedAppointments >= count.Count.Value
            || TerminatedFrom is { } terminated && next.Date >= terminated
            || End is SeriesEnd.OnDateInclusive ending && next.Date > ending.Date)
            return Result.Success<SeriesStep?, ErrorList>(null);
        var startsAt = SchoolTime.At(next.Date, next.StartHour.ToTimeOnly());
        if (startsAt.IsFailure)
            return ErrorList.From(startsAt.Error.Issues.Select(issue => new DomainIssue(new SchedulingError.TimeConversionFailed((TimeError)issue.Error))));
        var number = NextSequenceNumber.Next();
        if (number.IsFailure) return number.Error;
        var advanced = this with { NextSequenceNumber = number.Value, NextDate = next.Date == DateOnly.MaxValue ? null : next.Date.AddDays(1) };
        return new SeriesStep(new(NextSequenceNumber, startsAt.Value), advanced);
    }
}
