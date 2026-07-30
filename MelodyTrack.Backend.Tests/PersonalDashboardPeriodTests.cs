using MelodyTrack.Backend.Api.Dashboard;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class PersonalDashboardPeriodTests
{
    [Theory]
    [InlineData("2026-03-08T16:00:00Z", 23)]
    [InlineData("2026-11-01T17:00:00Z", 25)]
    public void Create_UsesLocalMidnightsAcrossDaylightSavingTransitions(string nowUtcValue, int expectedDayHours)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var nowUtc = DateTime.Parse(nowUtcValue, null, System.Globalization.DateTimeStyles.AdjustToUniversal);

        var period = PersonalDashboardPeriod.Create(timezone, nowUtc);

        (period.TomorrowStartUtc - period.TodayStartUtc).TotalHours.ShouldBe(expectedDayHours);
        period.Today.ShouldBe(DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timezone)));
        period.Tomorrow.ShouldBe(period.Today.AddDays(1));
    }
}
