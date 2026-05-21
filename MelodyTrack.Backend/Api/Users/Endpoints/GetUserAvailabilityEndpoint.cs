using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Users.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Users.Endpoints;

public class GetUserAvailabilityEndpoint(AppDbContext db, IUserAvailabilityService userAvailabilityService)
    : Ep.Req<GetUserAvailabilityRequest>.Res<Results<Ok<UserAvailabilityResponse>, UnauthorizedHttpResult, NotFound<ProblemDetails>>>
{
    public override void Configure()
    {
        Get("/users/{id}/availability");
    }

    public override async Task<Results<Ok<UserAvailabilityResponse>, UnauthorizedHttpResult, NotFound<ProblemDetails>>> ExecuteAsync(GetUserAvailabilityRequest req, CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
        if (login is null)
        {
            return TypedResults.Unauthorized();
        }

        var userExists = await db.Users
            .AsNoTracking()
            .AnyAsync(e => e.Id == req.Id, ct);

        if (!userExists)
        {
            AddError(r => r.Id, "Пользователь не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        var availability = await userAvailabilityService.GetAvailabilityAsync(req.Id, ct);
        return TypedResults.Ok(new UserAvailabilityResponse
        {
            UserId = req.Id,
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
    [BindFrom("id")]
    public Ulid Id { get; set; }
}
