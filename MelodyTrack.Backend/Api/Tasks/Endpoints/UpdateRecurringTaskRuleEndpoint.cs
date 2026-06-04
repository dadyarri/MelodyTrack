using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Tasks.Endpoints;

public class UpdateRecurringTaskRuleEndpoint(AppDbContext db, IEntityFreshnessService entityFreshnessService, IAuditLogService auditLogService)
    : Ep.Req<UpdateRecurringTaskRuleRequest>.Res<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Put("/tasks/rules/{id}");
    }

    public override async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ProblemDetails>, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(
        UpdateRecurringTaskRuleRequest req,
        CancellationToken ct)
    {
        var currentUser = await TaskAccess.GetCurrentUserAsync(User, db, ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!TaskAccess.CanAccessTasks(currentUser))
        {
            return TypedResults.Forbid();
        }

        var rule = await db.RecurringTaskRules.FirstOrDefaultAsync(item => item.Id == req.Id, ct);
        if (rule is null)
        {
            AddError(item => item.Id, "Правило не найдено");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        if (!IsTimingSupported(rule.Type, req))
        {
            AddError(item => item.OffsetMinutes, "Это поле недоступно для данного правила.");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "recurring_task_rule",
            rule.Id,
            req.ExpectedActivityId,
            "Правило было изменено другим пользователем. Обновите данные и повторите сохранение.",
            ct);

        if (conflict is not null && !IsNoOp(rule, req))
        {
            return TypedResults.Conflict(conflict);
        }

        var beforeIsEnabled = rule.IsEnabled;
        var beforeMessageTemplate = rule.MessageTemplate;
        var beforeOffsetMinutes = rule.OffsetMinutes;
        var beforeCooldownDays = rule.CooldownDays;

        rule.IsEnabled = req.IsEnabled;
        rule.MessageTemplate = req.MessageTemplate;
        rule.OffsetMinutes = SupportsOffsetMinutes(rule.Type) ? req.OffsetMinutes : null;
        rule.CooldownDays = SupportsCooldownDays(rule.Type) ? req.CooldownDays : null;
        rule.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "recurring_tasks",
            Action = "recurring_task_rule_updated",
            EntityType = "recurring_task_rule",
            EntityId = rule.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Правило", rule.Name),
                AuditDetailsFormatter.DescribeChange("Включено", beforeIsEnabled ? "Да" : "Нет", rule.IsEnabled ? "Да" : "Нет"),
                AuditDetailsFormatter.DescribeChange("Шаблон", beforeMessageTemplate, rule.MessageTemplate),
                AuditDetailsFormatter.DescribeChange("Смещение, мин", beforeOffsetMinutes?.ToString(), rule.OffsetMinutes?.ToString()),
                AuditDetailsFormatter.DescribeChange("Повтор, дней", beforeCooldownDays?.ToString(), rule.CooldownDays?.ToString())
            )
        }, ct);

        return TypedResults.NoContent();
    }

    private static bool IsNoOp(Data.Models.RecurringTaskRule rule, UpdateRecurringTaskRuleRequest req)
    {
        return rule.IsEnabled == req.IsEnabled
               && rule.MessageTemplate == req.MessageTemplate
               && rule.OffsetMinutes == (SupportsOffsetMinutes(rule.Type) ? req.OffsetMinutes : null)
               && rule.CooldownDays == (SupportsCooldownDays(rule.Type) ? req.CooldownDays : null);
    }

    private static bool IsTimingSupported(RecurringTaskType type, UpdateRecurringTaskRuleRequest req)
    {
        if (!SupportsOffsetMinutes(type) && req.OffsetMinutes is not null)
        {
            return false;
        }

        if (!SupportsCooldownDays(type) && req.CooldownDays is not null)
        {
            return false;
        }

        return true;
    }

    private static bool SupportsOffsetMinutes(RecurringTaskType type)
    {
        return type is RecurringTaskType.AppointmentReminder or RecurringTaskType.TrialFollowUp;
    }

    private static bool SupportsCooldownDays(RecurringTaskType type)
    {
        return type is RecurringTaskType.BirthdayGreeting or RecurringTaskType.InactiveClientReminder or RecurringTaskType.TeacherDailySchedule;
    }
}
