using MelodyTrack.Rewrite.Domain.Scheduling;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class SchoolTimeTests
{
    [Fact]
    public void At_MoscowEvening_ProducesUtcInstant()
    {
        var instant = Ok(SchoolTime.At(Date(5), new(18, 0)));
        Assert.Equal(Utc(5, 15), instant);
        Assert.Equal(TimeSpan.Zero, instant.Offset);
    }
    [Fact]
    public void Date_UtcEvening_UsesNextMoscowDate() => Assert.Equal(Date(6), SchoolTime.Date(Utc(5, 22)));
    [Fact]
    public void SameMonth_UtcMonthBoundary_UsesSchoolMonth() => Assert.True(SchoolTime.SameMonth(Utc(31, 22), new(2026, 2, 1, 1, 0, 0, TimeSpan.Zero)));
    [Fact]
    public void OnDate_OvernightWindow_EndsOnNextLocalDate()
    {
        var interval = Ok(Window(23, 2).OnDate(Date(5)));
        Assert.Equal(Utc(5, 20), interval.StartsAt);
        Assert.Equal(Utc(5, 23), interval.EndsAt);
    }
}
