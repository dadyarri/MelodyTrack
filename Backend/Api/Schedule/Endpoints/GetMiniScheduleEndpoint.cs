using Backend.Api.Schedule.Models;
using Backend.Data;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Schedule.Endpoints;

/// <summary>
/// Получить мини-расписание
/// </summary>
public class GetMiniScheduleEndpoint(AppDbContext db)
    : Endpoint<EmptyRequest, Results<Ok<GetMiniScheduleResponse>, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/api/schedule/mini");
    }

    /// <inheritdoc />
    public override async Task<Results<Ok<GetMiniScheduleResponse>, ProblemDetails>> ExecuteAsync(EmptyRequest req,
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
            .Select(sh => new
            {
                sh.StartDate,
                Service = sh.Service.Name,
                Name = $"{sh.Client.FirstName} {sh.Client.LastName}",
            })
            .OrderBy(sh => sh.StartDate)
            .ToListAsync(cancellationToken: ct);

        var result = new GetMiniScheduleResponse
        {
            Items = new Dictionary<string, List<MiniScheduleItem>>
            {
                { "Сегодня", [] },
                { "Завтра", [] }
            }
        };

        foreach (var item in scheduleData)
        {
            if (item.StartDate.Date == startOfTodayUtc.Date)
            {
                result.Items["Сегодня"].Add(new MiniScheduleItem
                {
                    Name = item.Name,
                    Service = item.Service,
                    Time = item.StartDate,
                });
            }
            else if (item.StartDate.Date == startOfTomorrowUtc.Date)
            {
                result.Items["Завтра"].Add(new MiniScheduleItem
                {
                    Name = item.Name,
                    Service = item.Service,
                    Time = item.StartDate,
                });
            }
        }

        return TypedResults.Ok(result);
    }
}