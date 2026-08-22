using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Tasks.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Tasks.Endpoints;

[ApiEndpoint(ApiMethod.Get, "/recurring-task-rules")]
public sealed class GetRecurringTaskRulesEndpoint
{

    public static async Task<Results<Ok<GetRecurringTaskRulesResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        AppDbContext db,
        IRecordActivityService recordActivityService,
        ICurrentUserAccessor currentUserAccessor,
        CancellationToken ct
    )
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!TaskAccess.CanAccessTasks(currentUser))
        {
            return TypedResults.Forbid();
        }

        var rules = await db.RecurringTaskRules
            .AsNoTracking()
            .OrderBy(rule => rule.Type)
            .ThenBy(rule => rule.Name)
            .Select(rule => new RecurringTaskRuleDto
            {
                Id = rule.Id,
                Name = rule.Name,
                Type = rule.Type.ToApiKey(),
                IsEnabled = rule.IsEnabled,
                MessageTemplate = rule.MessageTemplate,
                OffsetMinutes = rule.OffsetMinutes,
                CooldownDays = rule.CooldownDays,
                LastActivity = null
            })
            .ToListAsync(ct);

        var latestActivities = await recordActivityService.GetLatestActivitiesAsync("recurring_task_rule", rules.Select(rule => rule.Id.ToString()).ToList(), ct);
        foreach (var rule in rules)
        {
            rule.LastActivity = latestActivities.GetValueOrDefault(rule.Id.ToString());
        }

        return TypedResults.Ok(new GetRecurringTaskRulesResponse
        {
            Rules = rules
        });
    }
}
