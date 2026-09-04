using MelodyTrack.Backend.Api.Reports.Requests;
using MelodyTrack.Backend.Api.Reports.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Reports.Reporting;

public sealed record ReportContext(
    DateTime StartLocal,
    DateTime EndLocal,
    DateTime EndExclusiveLocal,
    DateTime StartUtc,
    DateTime EndExclusiveUtc,
    TimeZoneInfo Timezone,
    Ulid? ProviderId,
    string GroupBy);

public sealed record ReportContextResult(ReportContext? Context, string? Field, string? Error)
{
    public bool IsSuccess => Context is not null;
}

public interface IReportContextFactory
{
    ReportContextResult Create(GetReportRequest request, User currentUser);
    Task<ReportContextDto> CreateDtoAsync(ReportContext context, CancellationToken ct);
}

public sealed class ReportContextFactory(AppDbContext db) : IReportContextFactory
{
    private static readonly string[] Groupings = ["day", "week", "month"];

    public ReportContextResult Create(GetReportRequest request, User currentUser)
    {
        if (!currentUser.Role.RoleName.IsAnyAdmin())
        {
            return new(null, string.Empty, "Статистика доступна только администраторам.");
        }

        TimeZoneInfo timezone;
        try
        {
            timezone = TimeZoneInfo.FindSystemTimeZoneById(request.Timezone);
        }
        catch (TimeZoneNotFoundException)
        {
            return new(null, nameof(request.Timezone), "Часовой пояс не найден.");
        }
        catch (InvalidTimeZoneException)
        {
            return new(null, nameof(request.Timezone), "Часовой пояс недоступен.");
        }

        var start = request.Start.Date;
        var end = request.End.Date;
        if (end < start)
        {
            return new(null, nameof(request.End), "Дата окончания не может быть раньше даты начала.");
        }

        if ((end - start).TotalDays > 731)
        {
            return new(null, nameof(request.End), "Выберите период не длиннее двух лет.");
        }

        var groupBy = request.GroupBy.Trim().ToLowerInvariant();
        if (!Groupings.Contains(groupBy, StringComparer.Ordinal))
        {
            return new(null, nameof(request.GroupBy), "Доступная группировка: day, week или month.");
        }

        var endExclusive = end.AddDays(1);
        return new(new ReportContext(
            start,
            end,
            endExclusive,
            ReportBuckets.ToUtc(start, timezone),
            ReportBuckets.ToUtc(endExclusive, timezone),
            timezone,
            request.ProviderId,
            groupBy), null, null);
    }

    public async Task<ReportContextDto> CreateDtoAsync(ReportContext context, CancellationToken ct)
    {
        var providers = await db.Users.AsNoTracking()
            .Where(user => user.Role.RoleName != UserRoles.Client)
            .OrderBy(user => user.LastName)
            .ThenBy(user => user.FirstName)
            .Select(user => new ReportProviderDto
            {
                Id = user.Id,
                DisplayName = (user.LastName + " " + user.FirstName).Trim()
            })
            .ToListAsync(ct);

        var selectedProvider = context.ProviderId is { } providerId
            ? providers.FirstOrDefault(provider => provider.Id == providerId)
            : null;
        var scopeLabel = context.ProviderId is null
            ? "Вся организация"
            : selectedProvider?.DisplayName ?? "Выбранный преподаватель";

        return new ReportContextDto
        {
            StartDate = context.StartLocal,
            EndDate = context.EndLocal,
            Timezone = context.Timezone.Id,
            ProviderId = context.ProviderId,
            ScopeLabel = scopeLabel,
            GroupBy = context.GroupBy,
            Providers = providers
        };
    }
}

internal static class ReportBuckets
{
    public static DateTime Start(DateTime date, string groupBy) => groupBy switch
    {
        "week" => date.Date.AddDays(-MondayOffset(date.DayOfWeek)),
        "month" => new DateTime(date.Year, date.Month, 1),
        _ => date.Date
    };

    public static DateTime EndExclusive(DateTime start, string groupBy) => groupBy switch
    {
        "week" => start.AddDays(7),
        "month" => start.AddMonths(1),
        _ => start.AddDays(1)
    };

    public static List<DateTime> Starts(ReportContext context)
    {
        var result = new List<DateTime>();
        for (var cursor = Start(context.StartLocal, context.GroupBy); cursor < context.EndExclusiveLocal; cursor = EndExclusive(cursor, context.GroupBy))
        {
            result.Add(cursor);
        }

        return result;
    }

    public static decimal? Percent(decimal part, decimal total) => total == 0m ? null : part / total * 100m;

    public static DateTime ToUtc(DateTime local, TimeZoneInfo timezone)
    {
        while (timezone.IsInvalidTime(local))
        {
            local = local.AddMinutes(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), timezone);
    }

    private static int MondayOffset(DayOfWeek day) => day == DayOfWeek.Sunday ? 6 : (int)day - 1;
}
