using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Core.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public interface IUserAvailabilityService
{
    Task<UserAvailabilitySnapshot> GetAvailabilityAsync(Ulid userId, CancellationToken ct);
    Task<IReadOnlyList<UserAvailabilitySnapshot>> GetAvailabilitiesAsync(IReadOnlyCollection<Ulid>? userIds, CancellationToken ct);
    Task<bool> IsAvailableAsync(Ulid userId, DateTime startUtc, DateTime endUtc, string timezoneId, CancellationToken ct);
}

public record UserAvailabilitySnapshot(
    Ulid UserId,
    IReadOnlyList<UserWorkingHoursDaySnapshot> WorkingHours,
    IReadOnlyList<UserVacationSnapshot> Vacations,
    string Timezone = "UTC");

public record UserWorkingHoursDaySnapshot(
    DayOfWeek DayOfWeek,
    bool IsWorkingDay,
    int StartMinuteOfDay,
    int EndMinuteOfDay);

public record UserVacationSnapshot(
    Ulid Id,
    DateTime StartDate,
    DateTime EndDate,
    int Version = 1);

public class UserAvailabilityService(AppDbContext db) : IUserAvailabilityService
{
    public async Task<UserAvailabilitySnapshot> GetAvailabilityAsync(Ulid userId, CancellationToken ct)
    {
        var timezone = await db.Users
            .AsNoTracking()
            .Where(e => e.Id == userId)
            .Select(e => e.ScheduleTimezone)
            .SingleOrDefaultAsync(ct);

        if (timezone is null)
        {
            return new UserAvailabilitySnapshot(userId, [], []);
        }

        var workingHours = await db.UserWorkingHoursDays
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.DayOfWeek)
            .Select(e => new UserWorkingHoursDaySnapshot(
                e.DayOfWeek,
                e.IsWorkingDay,
                e.StartMinuteOfDay,
                e.EndMinuteOfDay))
            .ToListAsync(ct);

        var vacations = await db.Vacations
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.StartDate)
            .Select(e => new UserVacationSnapshot(e.Id, e.StartDate, e.EndDate, e.Version))
            .ToListAsync(ct);

        return new UserAvailabilitySnapshot(
            userId,
            workingHours.Count > 0 ? workingHours : GetDefaultWorkingHours(),
            vacations,
            timezone);
    }

    public async Task<IReadOnlyList<UserAvailabilitySnapshot>> GetAvailabilitiesAsync(IReadOnlyCollection<Ulid>? userIds, CancellationToken ct)
    {
        var usersQuery = db.Users.AsNoTracking();
        if (userIds is { Count: > 0 })
        {
            usersQuery = usersQuery.Where(user => userIds.Contains(user.Id));
        }

        var users = await usersQuery
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .Select(user => new { user.Id, user.ScheduleTimezone })
            .ToListAsync(ct);

        var userIdsList = users.Select(user => user.Id).ToList();
        var timezones = users.ToDictionary(user => user.Id, user => user.ScheduleTimezone);

        if (userIdsList.Count == 0)
        {
            return [];
        }

        var workingHours = await db.UserWorkingHoursDays
            .AsNoTracking()
            .Where(item => userIdsList.Contains(item.UserId))
            .OrderBy(item => item.UserId)
            .ThenBy(item => item.DayOfWeek)
            .Select(item => new
            {
                item.UserId,
                Snapshot = new UserWorkingHoursDaySnapshot(
                    item.DayOfWeek,
                    item.IsWorkingDay,
                    item.StartMinuteOfDay,
                    item.EndMinuteOfDay)
            })
            .ToListAsync(ct);

        var vacations = await db.Vacations
            .AsNoTracking()
            .Where(item => item.UserId != null && userIdsList.Contains(item.UserId.Value))
            .OrderBy(item => item.UserId)
            .ThenBy(item => item.StartDate)
            .Select(item => new
            {
                UserId = item.UserId!.Value,
                Snapshot = new UserVacationSnapshot(item.Id, item.StartDate, item.EndDate, item.Version)
            })
            .ToListAsync(ct);

        var workingHoursLookup = workingHours
            .GroupBy(item => item.UserId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<UserWorkingHoursDaySnapshot>)group.Select(item => item.Snapshot).ToList());
        var vacationsLookup = vacations
            .GroupBy(item => item.UserId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<UserVacationSnapshot>)group.Select(item => item.Snapshot).ToList());

        return userIdsList
            .Select(userId => new UserAvailabilitySnapshot(
                userId,
                workingHoursLookup.TryGetValue(userId, out var hours) && hours.Count > 0 ? hours : GetDefaultWorkingHours(),
                vacationsLookup.GetValueOrDefault(userId, []),
                timezones[userId]))
            .ToList();
    }

    public async Task<bool> IsAvailableAsync(Ulid userId, DateTime startUtc, DateTime endUtc, string timezoneId, CancellationToken ct)
    {
        var availability = await GetAvailabilityAsync(userId, ct);
        return IsAvailable(availability, startUtc, endUtc, availability.Timezone);
    }

    public static bool IsAvailable(UserAvailabilitySnapshot availability, DateTime startUtc, DateTime endUtc, string timezoneId)
    {
        return SchedulingAvailability.Evaluate(
            startUtc,
            endUtc,
            [],
            availability.Vacations.Select(item => new SchedulingVacation(item.Id, item.StartDate, item.EndDate)).ToList(),
            availability.WorkingHours.Select(item => new SchedulingWorkingDay(item.DayOfWeek, item.IsWorkingDay, item.StartMinuteOfDay, item.EndMinuteOfDay)).ToList(),
            timezoneId).Count == 0;
    }

    public static List<UserWorkingHoursDaySnapshot> GetDefaultWorkingHours()
    {
        return
        [
            new(DayOfWeek.Monday, true, 10 * 60, 20 * 60),
            new(DayOfWeek.Tuesday, true, 10 * 60, 20 * 60),
            new(DayOfWeek.Wednesday, true, 10 * 60, 20 * 60),
            new(DayOfWeek.Thursday, true, 10 * 60, 20 * 60),
            new(DayOfWeek.Friday, true, 10 * 60, 20 * 60),
            new(DayOfWeek.Saturday, false, 10 * 60, 20 * 60),
            new(DayOfWeek.Sunday, false, 10 * 60, 20 * 60)
        ];
    }
}
