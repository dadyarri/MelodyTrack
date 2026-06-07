using System.Security.Claims;
using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Users.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Users.Endpoints;

public class UpdateUserAvailabilityEndpoint(AppDbContext db, IEntityFreshnessService entityFreshnessService, IAuditLogService auditLogService)
    : Ep.Req<UpdateUserAvailabilityRequest>.Res<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Put("/users/{id}/availability");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(UpdateUserAvailabilityRequest req, CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
        if (login is null)
        {
            return TypedResults.Unauthorized();
        }

        var currentUser = await db.Users
            .Include(e => e.Role)
            .WhereEmailMatches(login)
            .FirstOrDefaultAsync(ct);

        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (currentUser.Id != req.Id && !currentUser.Role.RoleName.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var user = await db.Users
            .Include(e => e.Role)
            .Include(e => e.WorkingHours)
            .Include(e => e.Vacations)
            .FirstOrDefaultAsync(e => e.Id == req.Id, ct);

        if (user is null)
        {
            AddError(r => r.Id, "Пользователь не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        if (user.Role.RoleName.IsSuperuser() && !currentUser.Role.RoleName.IsSuperuser())
        {
            return TypedResults.Forbid();
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "user_availability",
            user.Id,
            req.ExpectedActivityId,
            "График работы был изменен другим пользователем. Обновите данные и повторите сохранение.",
            ct);

        if (conflict is not null && !IsNoOp(user, req))
        {
            return TypedResults.Conflict(conflict);
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
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "users",
            Action = "user_availability_updated",
            EntityType = "user_availability",
            EntityId = user.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Пользователь", $"{user.LastName} {user.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeContext("Рабочих дней", user.WorkingHours.Count(item => item.IsWorkingDay).ToString()),
                AuditDetailsFormatter.DescribeContext("Отпусков", user.Vacations.Count.ToString())
            )
        }, ct);
        return TypedResults.NoContent();
    }

    private static bool IsNoOp(User user, UpdateUserAvailabilityRequest req)
    {
        var currentWorkingHours = user.WorkingHours
            .OrderBy(item => item.DayOfWeek)
            .Select(item => new
            {
                DayOfWeek = MapDayOfWeek(item.DayOfWeek),
                item.IsWorkingDay,
                StartTime = item.IsWorkingDay ? FormatTime(item.StartMinuteOfDay) : null,
                EndTime = item.IsWorkingDay ? FormatTime(item.EndMinuteOfDay) : null
            })
            .ToList();

        var requestedWorkingHours = req.WorkingHours
            .OrderBy(item => item.DayOfWeek)
            .Select(item => new
            {
                DayOfWeek = item.DayOfWeek.Trim().ToLowerInvariant(),
                item.IsWorkingDay,
                StartTime = item.IsWorkingDay ? item.StartTime : null,
                EndTime = item.IsWorkingDay ? item.EndTime : null
            })
            .ToList();

        var currentVacations = user.Vacations
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.EndDate)
            .Select(item => new { item.StartDate, item.EndDate })
            .ToList();

        var requestedVacations = req.Vacations
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.EndDate)
            .Select(item => new { item.StartDate, item.EndDate })
            .ToList();

        return currentWorkingHours.SequenceEqual(requestedWorkingHours) && currentVacations.SequenceEqual(requestedVacations);
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
