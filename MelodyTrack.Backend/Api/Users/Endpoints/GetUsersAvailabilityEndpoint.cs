using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Users.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Users.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/users/availability")]
public sealed class GetUsersAvailabilityEndpoint
{

    public static async Task<Results<Ok<GetUsersAvailabilityResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        AppDbContext db,
        IUserAvailabilityService userAvailabilityService,
        IRecordActivityService recordActivityService,
        ICurrentUserAccessor currentUserAccessor,
        CancellationToken ct
    )
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null || !currentUser.Role.RoleName.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var availabilities = await userAvailabilityService.GetAvailabilitiesAsync(null, ct);
        if (!currentUser.Role.RoleName.IsSuperuser())
        {
            var superuserIds = await db.Users
                .AsNoTracking()
                .Where(user => user.Role.RoleName == UserRoles.Superuser)
                .Select(user => user.Id)
                .ToListAsync(ct);

            availabilities = availabilities
                .Where(availability => !superuserIds.Contains(availability.UserId))
                .ToList();
        }

        var latestActivities = await recordActivityService.GetLatestActivitiesAsync(
            "user_availability",
            availabilities.Select(availability => availability.UserId.ToString()).ToArray(),
            ct);

        return TypedResults.Ok(new GetUsersAvailabilityResponse
        {
            Availabilities = availabilities
                .Select(availability => MapAvailability(availability, latestActivities.GetValueOrDefault(availability.UserId.ToString())))
                .ToList()
        });
    }

    private static UserAvailabilityResponse MapAvailability(UserAvailabilitySnapshot availability, RecordActivityDto? lastActivity)
    {
        return new UserAvailabilityResponse
        {
            UserId = availability.UserId,
            LastActivity = lastActivity,
            WorkingHours = availability.WorkingHours
                .Select(item => new UserWorkingHoursDayDto
                {
                    DayOfWeek = MapDayOfWeek(item.DayOfWeek),
                    IsWorkingDay = item.IsWorkingDay,
                    StartTime = item.IsWorkingDay ? FormatTime(item.StartMinuteOfDay) : null,
                    EndTime = item.IsWorkingDay ? FormatTime(item.EndMinuteOfDay) : null
                })
                .ToList(),
            Vacations = availability.Vacations
                .Select(item => new UserVacationDto
                {
                    Id = item.Id,
                    StartDate = item.StartDate,
                    EndDate = item.EndDate
                })
                .ToList()
        };
    }

    private static string MapDayOfWeek(DayOfWeek value)
    {
        return value switch
        {
            DayOfWeek.Monday => "monday",
            DayOfWeek.Tuesday => "tuesday",
            DayOfWeek.Wednesday => "wednesday",
            DayOfWeek.Thursday => "thursday",
            DayOfWeek.Friday => "friday",
            DayOfWeek.Saturday => "saturday",
            _ => "sunday"
        };
    }

    private static string FormatTime(int totalMinutes)
    {
        return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
    }
}
