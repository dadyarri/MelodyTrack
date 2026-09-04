using MelodyTrack.Backend.Services;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class UserAvailabilityServiceTests
{
    [Fact]
    public void IsAvailable_PartialDayVacation_BlocksOnlyOverlappingTime()
    {
        var userId = Ulid.NewUlid();
        var vacation = new UserVacationSnapshot(
            Ulid.NewUlid(),
            new DateTime(2030, 6, 3, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2030, 6, 3, 14, 0, 0, DateTimeKind.Utc));
        var availability = new UserAvailabilitySnapshot(
            userId,
            [new UserWorkingHoursDaySnapshot(DayOfWeek.Monday, true, 9 * 60, 18 * 60)],
            [vacation]);

        var beforeVacation = UserAvailabilityService.IsAvailable(
            availability,
            new DateTime(2030, 6, 3, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2030, 6, 3, 11, 0, 0, DateTimeKind.Utc),
            "UTC");
        var overlappingVacation = UserAvailabilityService.IsAvailable(
            availability,
            new DateTime(2030, 6, 3, 13, 0, 0, DateTimeKind.Utc),
            new DateTime(2030, 6, 3, 14, 0, 0, DateTimeKind.Utc),
            "UTC");
        var adjacentToVacation = UserAvailabilityService.IsAvailable(
            availability,
            new DateTime(2030, 6, 3, 14, 0, 0, DateTimeKind.Utc),
            new DateTime(2030, 6, 3, 15, 0, 0, DateTimeKind.Utc),
            "UTC");

        beforeVacation.ShouldBeTrue();
        overlappingVacation.ShouldBeFalse();
        adjacentToVacation.ShouldBeTrue();
    }
}
