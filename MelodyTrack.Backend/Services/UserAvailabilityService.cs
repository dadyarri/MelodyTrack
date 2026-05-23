using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
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
    IReadOnlyList<UserVacationSnapshot> Vacations);

public record UserWorkingHoursDaySnapshot(
    DayOfWeek DayOfWeek,
    bool IsWorkingDay,
    int StartMinuteOfDay,
    int EndMinuteOfDay);

public record UserVacationSnapshot(
    Ulid Id,
    DateOnly StartDate,
    DateOnly EndDate);

public class UserAvailabilityService(AppDbContext db) : IUserAvailabilityService
{
    public async Task<UserAvailabilitySnapshot> GetAvailabilityAsync(Ulid userId, CancellationToken ct)
    {
        var userExists = await db.Users
            .AsNoTracking()
            .AnyAsync(e => e.Id == userId, ct);

        if (!userExists)
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

        var vacations = await db.UserVacations
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .OrderBy(e => e.StartDate)
            .Select(e => new UserVacationSnapshot(e.Id, e.StartDate, e.EndDate))
            .ToListAsync(ct);

        return new UserAvailabilitySnapshot(
            userId,
            workingHours.Count > 0 ? workingHours : GetDefaultWorkingHours(),
            vacations);
    }

    public async Task<IReadOnlyList<UserAvailabilitySnapshot>> GetAvailabilitiesAsync(IReadOnlyCollection<Ulid>? userIds, CancellationToken ct)
    {
        var usersQuery = db.Users.AsNoTracking();
        if (userIds is { Count: > 0 })
        {
            usersQuery = usersQuery.Where(user => userIds.Contains(user.Id));
        }

        var userIdsList = await usersQuery
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .Select(user => user.Id)
            .ToListAsync(ct);

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

        var vacations = await db.UserVacations
            .AsNoTracking()
            .Where(item => userIdsList.Contains(item.UserId))
            .OrderBy(item => item.UserId)
            .ThenBy(item => item.StartDate)
            .Select(item => new
            {
                item.UserId,
                Snapshot = new UserVacationSnapshot(item.Id, item.StartDate, item.EndDate)
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
                vacationsLookup.GetValueOrDefault(userId, [])))
            .ToList();
    }

    public async Task<bool> IsAvailableAsync(Ulid userId, DateTime startUtc, DateTime endUtc, string timezoneId, CancellationToken ct)
    {
        var availability = await GetAvailabilityAsync(userId, ct);
        return IsAvailable(availability, startUtc, endUtc, timezoneId);
    }

    public static bool IsAvailable(UserAvailabilitySnapshot availability, DateTime startUtc, DateTime endUtc, string timezoneId)
    {
        var localStart = DateTimeUtils.ConvertDateToTimezone(startUtc, timezoneId);
        var localEnd = DateTimeUtils.ConvertDateToTimezone(endUtc, timezoneId);

        if (localStart.Date != localEnd.Date)
        {
            return false;
        }

        var localDate = DateOnly.FromDateTime(localStart);
        if (availability.Vacations.Any(vacation => vacation.StartDate <= localDate && vacation.EndDate >= localDate))
        {
            return false;
        }

        var workingDay = availability.WorkingHours.FirstOrDefault(e => e.DayOfWeek == localStart.DayOfWeek);
        if (workingDay is null)
        {
            return true;
        }

        if (!workingDay.IsWorkingDay)
        {
            return false;
        }

        var startMinuteOfDay = localStart.Hour * 60 + localStart.Minute;
        var endMinuteOfDay = localEnd.Hour * 60 + localEnd.Minute;

        return startMinuteOfDay >= workingDay.StartMinuteOfDay
               && endMinuteOfDay <= workingDay.EndMinuteOfDay;
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
