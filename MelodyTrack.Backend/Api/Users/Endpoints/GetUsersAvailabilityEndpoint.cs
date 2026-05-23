using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Users.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Users.Endpoints;

public class GetUsersAvailabilityEndpoint(AppDbContext db, IUserAvailabilityService userAvailabilityService)
    : Ep.NoReq.Res<Results<Ok<GetUsersAvailabilityResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/users/availability");
    }

    public override async Task<Results<Ok<GetUsersAvailabilityResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Name)?.Value;
        if (login is null)
        {
            return TypedResults.Unauthorized();
        }

        var currentUser = await db.Users
            .AsNoTracking()
            .Where(user => user.Email == login)
            .Include(user => user.Role)
            .FirstOrDefaultAsync(ct);

        if (currentUser is null || !currentUser.Role.RoleName.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var availabilities = await userAvailabilityService.GetAvailabilitiesAsync(null, ct);
        return TypedResults.Ok(new GetUsersAvailabilityResponse
        {
            Availabilities = availabilities.Select(MapAvailability).ToList()
        });
    }

    private static UserAvailabilityResponse MapAvailability(UserAvailabilitySnapshot availability)
    {
        return new UserAvailabilityResponse
        {
            UserId = availability.UserId,
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
