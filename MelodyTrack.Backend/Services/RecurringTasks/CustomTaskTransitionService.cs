using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services.RecurringTasks;

public interface ICustomTaskTransitionService
{
    Task<RecurringTaskActionResult> CompleteAsync(CompleteRecurringTaskRequest request, User actor, CancellationToken ct);
    Task<RecurringTaskActionResult> CancelAsync(CancelRecurringTaskRequest request, User actor, CancellationToken ct);
    Task<RecurringTaskActionResult> DelayAsync(
        DelayRecurringTaskRequest request,
        DateTime delayUntilUtc,
        User actor,
        CancellationToken ct);
}

internal sealed class CustomTaskTransitionService(
    AppDbContext db,
    IAuditLogService auditLogService,
    TimeProvider timeProvider) : ICustomTaskTransitionService
{
    public async Task<RecurringTaskActionResult> CompleteAsync(
        CompleteRecurringTaskRequest request,
        User actor,
        CancellationToken ct)
    {
        var task = await FindTaskAsync(request.RuleId, ct);
        var validationFailure = Validate(task, request.DeduplicationKey, rejectActiveDelay: true);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        task!.CompletedAtUtc = nowUtc;
        task.CompletedByUserId = actor.Id;
        task.CancelledAtUtc = null;
        task.CancelledByUserId = null;
        ClearDelay(task);

        await SaveAndAuditAsync(task, "task_completed", null, ct);
        return RecurringTaskActionResult.Success(RecurringTaskStatus.Completed);
    }

    public async Task<RecurringTaskActionResult> CancelAsync(
        CancelRecurringTaskRequest request,
        User actor,
        CancellationToken ct)
    {
        var task = await FindTaskAsync(request.RuleId, ct);
        var validationFailure = Validate(task, request.DeduplicationKey, rejectActiveDelay: true);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        task!.CompletedAtUtc = null;
        task.CompletedByUserId = null;
        task.CancelledAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        task.CancelledByUserId = actor.Id;
        ClearDelay(task);

        await SaveAndAuditAsync(task, "task_cancelled", null, ct);
        return RecurringTaskActionResult.Success(RecurringTaskStatus.Cancelled);
    }

    public async Task<RecurringTaskActionResult> DelayAsync(
        DelayRecurringTaskRequest request,
        DateTime delayUntilUtc,
        User actor,
        CancellationToken ct)
    {
        var task = await FindTaskAsync(request.RuleId, ct);
        var validationFailure = Validate(task, request.DeduplicationKey, rejectActiveDelay: false);
        if (validationFailure is not null)
        {
            return validationFailure;
        }

        task!.CompletedAtUtc = null;
        task.CompletedByUserId = null;
        task.CancelledAtUtc = null;
        task.CancelledByUserId = null;
        task.DelayedAtUtc = timeProvider.GetUtcNow().UtcDateTime;
        task.DelayedByUserId = actor.Id;
        task.DelayedUntilUtc = delayUntilUtc;

        await SaveAndAuditAsync(
            task,
            "task_delayed",
            AuditDetailsFormatter.DescribeContext("Отложено до", delayUntilUtc),
            ct);
        return RecurringTaskActionResult.Success(RecurringTaskStatus.Delayed);
    }

    private Task<CustomTask?> FindTaskAsync(Ulid ruleId, CancellationToken ct)
    {
        return db.CustomTasks
            .Include(item => item.Client)
            .ThenInclude(client => client!.Contacts)
            .FirstOrDefaultAsync(item => item.Id == ruleId, ct);
    }

    private RecurringTaskActionResult? Validate(
        CustomTask? task,
        string deduplicationKey,
        bool rejectActiveDelay)
    {
        if (task is null
            || RecurringTaskPresentationMapper.BuildCustomTaskDeduplicationKey(task.Id) != deduplicationKey)
        {
            return RecurringTaskActionResult.Failure("Задача больше не актуальна.");
        }

        if (task.CompletedAtUtc is not null || task.CancelledAtUtc is not null)
        {
            return RecurringTaskActionResult.Failure("Задача уже обработана другим пользователем.");
        }

        if (rejectActiveDelay
            && task.DelayedUntilUtc is { } delayedUntilUtc
            && delayedUntilUtc > timeProvider.GetUtcNow().UtcDateTime)
        {
            return RecurringTaskActionResult.Failure("Задача уже отложена на более позднее время.");
        }

        return null;
    }

    private async Task SaveAndAuditAsync(
        CustomTask task,
        string action,
        string? extraDetails,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "recurring_tasks",
            Action = action,
            EntityType = "custom_task",
            EntityId = task.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                BuildAuditDetails(task),
                extraDetails)
        }, ct);
        await transaction.CommitAsync(ct);
    }

    private static void ClearDelay(CustomTask task)
    {
        task.DelayedAtUtc = null;
        task.DelayedByUserId = null;
        task.DelayedUntilUtc = null;
    }

    private static string BuildAuditDetails(CustomTask task)
    {
        return AuditDetailsFormatter.JoinChanges(
            AuditDetailsFormatter.DescribeContext("Тип", RecurringTaskType.CustomTask.ToDisplayLabel()),
            AuditDetailsFormatter.DescribeContext("Задача", task.Title),
            AuditDetailsFormatter.DescribeContext("Получатель", task.RecipientName),
            AuditDetailsFormatter.DescribeContext("Дата", task.DueAtUtc));
    }
}
