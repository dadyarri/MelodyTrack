using System.Security.Claims;
using Backend.Api.Base.Models;
using Backend.Api.Schedule.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using ProblemDetails = FastEndpoints.ProblemDetails;

namespace Backend.Api.Schedule.Endpoints;

/// <summary>
/// Получить расписание
/// </summary>
/// <param name="dbContext">БД</param>
public class GetScheduleEndpoint(AppDbContext dbContext)
    : Endpoint<GetScheduleRequest, Results<Ok<List<ServiceHistory>>, ForbidHttpResult, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/api/schedule");
    }

    /// <inheritdoc />
    public override async Task<Results<Ok<List<ServiceHistory>>, ForbidHttpResult, ProblemDetails>>
        ExecuteAsync(
            GetScheduleRequest req,
            CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (login is null) return TypedResults.Forbid();

        var user = await dbContext.Users.Where(u => u.Username == login.Value).FirstOrDefaultAsync(ct);

        if (user is null) return TypedResults.Forbid();

        var query = dbContext.Schedule
            .Include(sh => sh.Service)
            .Include(sh => sh.Client)
            .Where(sh => sh.StartDate >= req.StartDate && sh.StartDate <= req.EndDate && sh.Service.Provider == user)
            .OrderBy(sh => sh.StartDate);

        var items = await query.ToListAsync(ct);
        var tz = TimeZoneInfo.FindSystemTimeZoneById(req.Timezone);

        foreach (var item in items)
        {
            item.StartDate = ConvertDateToTimezone(item.StartDate, tz);
            item.EndDate = ConvertDateToTimezone(item.EndDate, tz);
        }

        return TypedResults.Ok(items);
    }

    private DateTime ConvertDateToTimezone(DateTime date, TimeZoneInfo tz)
    {
        if (date.Kind == DateTimeKind.Unspecified)
        {
            date = DateTime.SpecifyKind(date, DateTimeKind.Utc);
        }
        else if (date.Kind == DateTimeKind.Local)
        {
            date = date.ToUniversalTime();
        }
        return TimeZoneInfo.ConvertTimeFromUtc(date, tz);
    }
}