using MelodyTrack.Core.Scheduling;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class SchedulingAvailabilityTests
{
    [Fact]
    public void Evaluate_AdjacentVacation_DoesNotBlockSlot()
    {
        var start = Utc(12);
        var vacation = new SchedulingVacation(Ulid.NewUlid(), Utc(11), start);

        var blockers = SchedulingAvailability.Evaluate(start, Utc(13), [vacation], [], null, "UTC");

        blockers.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(true, "client_vacation")]
    [InlineData(false, "provider_vacation")]
    public void Evaluate_OverlappingVacation_IdentifiesUnavailableParticipant(bool client, string code)
    {
        var vacation = new SchedulingVacation(Ulid.NewUlid(), Utc(12).AddMinutes(30), Utc(14));

        var blockers = SchedulingAvailability.Evaluate(Utc(12), Utc(13), client ? [vacation] : [], client ? [] : [vacation], null, "UTC");

        blockers.ShouldHaveSingleItem().Code.ShouldBe(code);
        blockers[0].VacationId.ShouldBe(vacation.Id);
    }

    [Fact]
    public void Evaluate_LocalMondayAcrossUtcMidnight_UsesTeacherWorkingDay()
    {
        var start = new DateTime(2030, 6, 2, 23, 0, 0, DateTimeKind.Utc);
        var hours = new[] { new SchedulingWorkingDay(DayOfWeek.Monday, true, 120, 240) };

        var blockers = SchedulingAvailability.Evaluate(start, start.AddHours(1), [], [], hours, "Europe/Moscow");

        blockers.ShouldBeEmpty();
    }

    [Fact]
    public void Evaluate_EndAfterWorkingBoundaryByOneSecond_RejectsSlot()
    {
        var hours = new[] { new SchedulingWorkingDay(DayOfWeek.Monday, true, 600, 780) };

        var blockers = SchedulingAvailability.Evaluate(Utc(12), Utc(13).AddSeconds(1), [], [], hours, "UTC");

        blockers.ShouldHaveSingleItem().Code.ShouldBe("provider_working_hours");
    }

    [Fact]
    public void Evaluate_MissingWorkingDay_RejectsSlot()
    {
        var blockers = SchedulingAvailability.Evaluate(Utc(12), Utc(13), [], [], [], "UTC");

        blockers.ShouldHaveSingleItem().Code.ShouldBe("provider_day_off");
    }

    [Fact]
    public void Evaluate_OverlappingClientAppointment_RejectsSlotWithoutProvider()
    {
        var occupied = new SchedulingBusyInterval(Ulid.NewUlid(), Utc(12).AddMinutes(30), Utc(14));

        var blockers = SchedulingAvailability.Evaluate(Utc(12), Utc(13), [], [], null, "UTC", [occupied]);

        blockers.ShouldHaveSingleItem().Code.ShouldBe("client_busy");
        blockers[0].AppointmentId.ShouldBe(occupied.AppointmentId);
    }


    [Fact]
    public void GetWorkingIntervals_StartInsideMissingHour_RetainsRemainingWorkingTime()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        var intervals = SchedulingAvailability.GetWorkingIntervals(new DateTime(2030, 3, 31), 150, 240, zone);

        intervals.ShouldHaveSingleItem().ShouldBe((new DateTime(2030, 3, 31, 1, 0, 0, DateTimeKind.Utc),
            new DateTime(2030, 3, 31, 2, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public void Evaluate_RepeatedLocalHour_DoesNotAllowCrossingMinutesOutsideWorkingWindow()
    {
        var start = new DateTime(2030, 10, 27, 0, 20, 0, DateTimeKind.Utc);
        var hours = new[] { new SchedulingWorkingDay(DayOfWeek.Sunday, true, 120, 150) };

        var blockers = SchedulingAvailability.Evaluate(start, start.AddHours(1), [], [], hours, "Europe/Berlin");
        var windows = SchedulingAvailability.GetWorkingIntervals(start.Date, 120, 150, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"));

        blockers.ShouldHaveSingleItem().Code.ShouldBe("provider_working_hours");
        windows.Count.ShouldBe(2);
        windows.Sum(item => (item.EndUtc - item.StartUtc).TotalMinutes).ShouldBe(60);
    }

    private static DateTime Utc(int hour) => new(2030, 6, 3, hour, 0, 0, DateTimeKind.Utc);
}
