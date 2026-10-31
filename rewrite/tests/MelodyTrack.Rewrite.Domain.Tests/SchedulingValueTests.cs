using MelodyTrack.Rewrite.Application;

using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class SchedulingValueTests
{
    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public void Create_NonPositiveSequence_RejectsValue(long value) => AssertError(new SchedulingError.InvalidSequenceNumber(value), SequenceNumber.Create(value));
    [Fact]
    public void Next_MaxSequence_RejectsOverflow() => AssertError(new SchedulingError.SequenceNumberExhausted(), Number(long.MaxValue).Next());
    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void Create_StartHourOutsideDay_RejectsValue(int value) => AssertError(new SchedulingError.InvalidStartHour(value), StartHour.Create(value));
    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(75)]
    public void Create_UnsupportedDuration_RejectsValue(int value) => AssertError(new SchedulingError.InvalidLessonDuration(value), LessonDuration.Create(value));
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveAppointmentCount_RejectsValue(int value) => AssertError(new SchedulingError.InvalidAppointmentCount(value), AppointmentCount.Create(value));
    [Fact]
    public void Create_EmptyWeeklyRule_RejectsRule() => AssertError(new SchedulingError.EmptyWeeklyRule(), WeeklyRule.Create([]));
    [Fact]
    public void Create_DuplicateWeekday_RejectsEvenDifferentHours() => AssertError(new SchedulingError.DuplicateWeekday(Weekday.Monday),
        WeeklyRule.Create([new(Weekday.Monday, Ok(StartHour.Create(18))), new(Weekday.Monday, Ok(StartHour.Create(19)))]));
    [Fact]
    public void NextOnOrAfter_StartingBetweenSlots_SelectsNextWeekdayAndItsHour()
    {
        var next = Assert.IsType<WeeklyOccurrence>(Weekly.NextOnOrAfter(Date(6)));
        Assert.Equal(Date(8), next.Date);
        Assert.Equal(19, next.StartHour.Value);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(45)]
    [InlineData(60)]
    [InlineData(90)]
    [InlineData(120)]
    public void Create_SupportedDuration_PreservesMinutes(int minutes) => Assert.Equal(TimeSpan.FromMinutes(minutes), Duration(minutes).ToTimeSpan());
}
