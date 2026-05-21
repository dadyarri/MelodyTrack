using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Users.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Users.Endpoints;

public class UpdateUserAvailabilityEndpoint(AppDbContext db)
    : Ep.Req<UpdateUserAvailabilityRequest>.Res<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>>>
{
    public override void Configure()
    {
        Put("/users/{id}/availability");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>>> ExecuteAsync(UpdateUserAvailabilityRequest req, CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
        if (login is null)
        {
            return TypedResults.Unauthorized();
        }

        var currentUser = await db.Users
            .Include(e => e.Role)
            .FirstOrDefaultAsync(e => e.Email == login, ct);

        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (currentUser.Id != req.Id && !currentUser.Role.RoleName.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var user = await db.Users
            .Include(e => e.WorkingHours)
            .Include(e => e.Vacations)
            .FirstOrDefaultAsync(e => e.Id == req.Id, ct);

        if (user is null)
        {
            AddError(r => r.Id, "Пользователь не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        db.UserWorkingHoursDays.RemoveRange(user.WorkingHours);
        db.UserVacations.RemoveRange(user.Vacations);

        user.WorkingHours = req.WorkingHours
            .Select(item => new UserWorkingHoursDay
            {
                Id = Ulid.NewUlid(),
                UserId = user.Id,
                User = user,
                DayOfWeek = ParseDayOfWeek(item.DayOfWeek),
                IsWorkingDay = item.IsWorkingDay,
                StartMinuteOfDay = item.IsWorkingDay && TimeOnly.TryParse(item.StartTime, out var startTime)
                    ? startTime.Hour * 60 + startTime.Minute
                    : 10 * 60,
                EndMinuteOfDay = item.IsWorkingDay && TimeOnly.TryParse(item.EndTime, out var endTime)
                    ? endTime.Hour * 60 + endTime.Minute
                    : 20 * 60
            })
            .ToList();

        user.Vacations = req.Vacations
            .Select(item => new UserVacation
            {
                Id = Ulid.NewUlid(),
                UserId = user.Id,
                User = user,
                StartDate = item.StartDate,
                EndDate = item.EndDate
            })
            .ToList();

        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    private static DayOfWeek ParseDayOfWeek(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "monday" => DayOfWeek.Monday,
            "tuesday" => DayOfWeek.Tuesday,
            "wednesday" => DayOfWeek.Wednesday,
            "thursday" => DayOfWeek.Thursday,
            "friday" => DayOfWeek.Friday,
            "saturday" => DayOfWeek.Saturday,
            _ => DayOfWeek.Sunday
        };
    }
}
