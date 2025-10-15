using Backend.Api.Schedule.Models;
using Backend.Data;
using Backend.Utils;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Schedule.Endpoints;

/// <summary>
///     Получить мини-расписание
/// </summary>
public class GetMiniScheduleEndpoint(AppDbContext db)
    : Endpoint<GetMiniScheduleRequest, Results<Ok<GetMiniScheduleResponse>, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/schedule/mini");
    }

    /// <inheritdoc />
    public override async Task<Results<Ok<GetMiniScheduleResponse>, ProblemDetails>> ExecuteAsync(
        GetMiniScheduleRequest req,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var startOfTodayUtc = now.Date;
        var startOfTomorrowUtc = startOfTodayUtc.AddDays(1);
        var startOfDayAfterTomorrowUtc = startOfTomorrowUtc.AddDays(1);
        var scheduleData = await db.Schedule
            .Where(sh => (sh.StartDate >= startOfTodayUtc && sh.StartDate < startOfTomorrowUtc) ||
                         (sh.StartDate >= startOfTomorrowUtc && sh.StartDate < startOfDayAfterTomorrowUtc))
            .Include(sh => sh.Client)
            .Include(sh => sh.Service)
            .OrderBy(sh => sh.StartDate)
            .ThenBy(sh => sh.Client.LastName)
            .ThenBy(sh => sh.Client.FirstName)
            .Select(sh => new
            {
                sh.StartDate,
                Service = sh.Service.Name,
                Name = $"{sh.Client.LastName} {sh.Client.FirstName}"
            })
            .ToListAsync(ct);

        var result = new GetMiniScheduleResponse
        {
            Items = new Dictionary<string, List<MiniScheduleItem>>
            {
                { "Сегодня", [] },
                { "Завтра", [] }
            }
        };
        var tz = TimeZoneInfo.FindSystemTimeZoneById(req.Timezone);

        foreach (var item in scheduleData)
            if (item.StartDate.Date == startOfTodayUtc.Date)
                result.Items["Сегодня"].Add(new MiniScheduleItem
                {
                    Name = item.Name,
                    Service = item.Service,
                    Time = DateTimeUtils.ConvertDateToTimezone(item.StartDate, tz)
                });
            else if (item.StartDate.Date == startOfTomorrowUtc.Date)
                result.Items["Завтра"].Add(new MiniScheduleItem
                {
                    Name = item.Name,
                    Service = item.Service,
                    Time = DateTimeUtils.ConvertDateToTimezone(item.StartDate, tz)
                });

        return TypedResults.Ok(result);
    }
}