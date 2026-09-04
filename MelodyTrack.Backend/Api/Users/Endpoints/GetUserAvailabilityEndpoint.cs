using MelodyTrack.Backend.ErrorHandling;
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

[ApiEndpoint(ApiMethod.Get, "/users/{id}/availability")]
public sealed class GetUserAvailabilityEndpoint
{

    public static async Task<Results<Ok<UserAvailabilityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> HandleAsync(
        [AsParameters] GetUserAvailabilityRequest req,
        AppDbContext db,
        IUserAvailabilityService userAvailabilityService,
        IRecordActivityService recordActivityService,
        ICurrentUserAccessor currentUserAccessor,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (currentUser.Id != req.Id && !currentUser.Role.RoleName.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var userExists = await db.Users
            .AsNoTracking()
            .Where(e => e.Id == req.Id)
            .Select(e => new { e.Id, RoleName = e.Role.RoleName })
            .FirstOrDefaultAsync(ct);

        if (userExists is null)
        {
            validationErrors.Add(nameof(req.Id), "Пользователь не найден");
            return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
        }

        if (userExists.RoleName == UserRoles.Superuser && !currentUser.Role.RoleName.IsSuperuser())
        {
            return TypedResults.Forbid();
        }

        var availability = await userAvailabilityService.GetAvailabilityAsync(req.Id, ct);
        return TypedResults.Ok(new UserAvailabilityResponse
        {
            UserId = req.Id,
            LastActivity = await recordActivityService.GetLatestActivityAsync("user_availability", req.Id.ToString(), ct),
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
        });
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

public class GetUserAvailabilityRequest
{
    [FromQuery(Name = "id")]
    [FromRoute]
    public Ulid Id { get; set; }
}
