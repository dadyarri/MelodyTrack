
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class TimeIntervalTests
{
    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public void Create_EndNotAfterStart_RejectsInterval(int endHour) => AssertError(new TimeError.EmptyOrReversedInterval(), TimeInterval.Create(Utc(5, 10), Utc(5, endHour)));

    [Fact]
    public void Create_OffsetInput_NormalizesBothBoundaries()
    {
        var start = new DateTimeOffset(2026, 1, 5, 18, 0, 0, TimeSpan.FromHours(3));
        var interval = Period(start, start.AddMinutes(30));
        Assert.Equal(TimeSpan.Zero, interval.StartsAt.Offset);
        Assert.Equal(TimeSpan.Zero, interval.EndsAt.Offset);
        Assert.Equal(Utc(5, 15), interval.StartsAt);
        Assert.Equal(TimeSpan.FromMinutes(30), interval.Duration);
    }

    [Fact]
    public void Overlaps_AdjacentIntervals_ReturnsFalse() => Assert.False(Period(Utc(5, 10), Utc(5, 11)).Overlaps(Period(Utc(5, 11), Utc(5, 12))));
    [Fact]
    public void Overlaps_OneTickIntersection_ReturnsTrue() => Assert.True(Period(Utc(5, 10), Utc(5, 11)).Overlaps(Period(Utc(5, 11).AddTicks(-1), Utc(5, 12))));

    [Fact]
    public void Contains_EndBoundary_IsExcluded()
    {
        var interval = Period(Utc(5, 10), Utc(5, 11));
        Assert.True(interval.Contains(Utc(5, 10)));
        Assert.False(interval.Contains(Utc(5, 11)));
    }

    [Fact]
    public void Covers_AdjacentUnorderedWindows_CoversCombinedInterval() => Assert.True(Period(Utc(5, 10), Utc(5, 12)).IsCoveredBy([Period(Utc(5, 11), Utc(5, 12)), Period(Utc(5, 10), Utc(5, 11))]));
    [Fact]
    public void Covers_GapBetweenWindows_ReturnsFalse() => Assert.False(Period(Utc(5, 10), Utc(5, 12)).IsCoveredBy([Period(Utc(5, 10), Utc(5, 11).AddTicks(-1)), Period(Utc(5, 11), Utc(5, 12))]));
}
