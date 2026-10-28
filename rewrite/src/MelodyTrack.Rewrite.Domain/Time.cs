using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Scheduling;

public static class SchoolTime
{
    private static readonly Lazy<TimeZoneInfo> Zone = new(() => TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow"));

    public static DateTimeOffset ToLocal(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, Zone.Value);
    public static DateOnly Date(DateTimeOffset instant) => DateOnly.FromDateTime(ToLocal(instant).DateTime);
    public static bool IsWholeHour(DateTimeOffset instant) => ToLocal(instant).TimeOfDay.Ticks % TimeSpan.TicksPerHour == 0;
    public static bool SameMonth(DateTimeOffset left, DateTimeOffset right) => Date(left).Year == Date(right).Year && Date(left).Month == Date(right).Month;

    public static Result<DateTimeOffset, ErrorList> At(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        if (Zone.Value.IsInvalidTime(local)) return ErrorList.Of(new TimeError.InvalidLocalTime());
        if (Zone.Value.IsAmbiguousTime(local)) return ErrorList.Of(new TimeError.AmbiguousLocalTime());
        try
        {
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, Zone.Value));
        }
        catch (ArgumentException)
        {
            return ErrorList.Of(new TimeError.OutOfRange());
        }
    }
}

public sealed record TimeInterval
{
    private TimeInterval(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        StartsAt = startsAt;
        EndsAt = endsAt;
    }

    public DateTimeOffset StartsAt { get; }
    public DateTimeOffset EndsAt { get; }
    public TimeSpan Duration => EndsAt - StartsAt;

    public static Result<TimeInterval, ErrorList> Create(DateTimeOffset startsAt, DateTimeOffset endsAt)
    {
        if (endsAt <= startsAt) return ErrorList.Of(new TimeError.EmptyOrReversedInterval());
        return new TimeInterval(startsAt.ToUniversalTime(), endsAt.ToUniversalTime());
    }

    public bool Overlaps(TimeInterval other) => StartsAt < other.EndsAt && other.StartsAt < EndsAt;
    public bool Contains(DateTimeOffset instant) => StartsAt <= instant && instant < EndsAt;
    public bool ContainsInterval(TimeInterval inner) => StartsAt <= inner.StartsAt && inner.EndsAt <= EndsAt;

    public bool IsCoveredBy(IEnumerable<TimeInterval> available)
    {
        var coveredThrough = StartsAt;
        foreach (var window in available.OrderBy(window => window.StartsAt))
        {
            if (window.StartsAt <= coveredThrough && window.EndsAt > coveredThrough) coveredThrough = window.EndsAt;
        }
        return coveredThrough >= EndsAt;
    }
}
