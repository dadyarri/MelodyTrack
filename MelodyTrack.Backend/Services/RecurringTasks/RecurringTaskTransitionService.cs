using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Api.Tasks.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services.RecurringTasks;

public interface IRecurringTaskService
{
    Task<List<RecurringTaskDto>> GetTasksAsync(string timezone, RecurringTaskType? filterType, RecurringTaskListStatus status, CancellationToken ct);
    Task<RecurringTaskActionResult> CompleteAsync(CompleteRecurringTaskRequest request, User actor, CancellationToken ct);
    Task<RecurringTaskActionResult> CancelAsync(CancelRecurringTaskRequest request, User actor, CancellationToken ct);
    Task<RecurringTaskActionResult> DelayAsync(DelayRecurringTaskRequest request, User actor, CancellationToken ct);
}

internal interface IRecurringTaskTransitionService
{
    Task<RecurringTaskActionResult> CompleteAsync(
        CompleteRecurringTaskRequest request,
        User actor,
        CancellationToken ct);
    Task<RecurringTaskActionResult> CancelAsync(
        CancelRecurringTaskRequest request,
        User actor,
        CancellationToken ct);
    Task<RecurringTaskActionResult> DelayAsync(
        DelayRecurringTaskRequest request,
        User actor,
        CancellationToken ct);
}

public sealed class RecurringTaskActionResult
{
    public required bool Succeeded { get; init; }
    public required string ErrorMessage { get; init; }
    public required RecurringTaskStatus? Status { get; init; }

    public static RecurringTaskActionResult Success(RecurringTaskStatus status) =>
        new()
        {
            Succeeded = true,
            ErrorMessage = string.Empty,
            Status = status
        };

    public static RecurringTaskActionResult Failure(string message) =>
        new()
        {
            Succeeded = false,
            ErrorMessage = message,
            Status = null
        };
}

internal sealed class RecurringTaskTransitionService(
    AppDbContext db,
    IAuditLogService auditLogService,
    TimeProvider timeProvider,
    ICustomTaskTransitionService customTaskTransitions,
    IRecurringTaskCandidateService candidateService) : IRecurringTaskTransitionService
{
    private DateTime UtcNow => timeProvider.GetUtcNow().UtcDateTime;



    public async Task<RecurringTaskActionResult> CompleteAsync(CompleteRecurringTaskRequest request, User actor, CancellationToken ct)
    {
        if (!RecurringTaskTypeExtensions.TryParseApiKey(request.Type, out var type))
        {
            return RecurringTaskActionResult.Failure("Неизвестный тип задачи.");
        }

        if (type == RecurringTaskType.CustomTask)
        {
            return await customTaskTransitions.CompleteAsync(request, actor, ct);
        }

        var existingExecution = await db.RecurringTaskExecutions
            .FirstOrDefaultAsync(execution => execution.DeduplicationKey == request.DeduplicationKey, ct);

        if (existingExecution is { Status: not RecurringTaskStatus.Delayed })
        {
            return RecurringTaskActionResult.Failure("Задача уже обработана другим пользователем.");
        }

        if (existingExecution is { Status: RecurringTaskStatus.Delayed, DelayedUntilUtc: { } delayedUntilUtc } && delayedUntilUtc > UtcNow)
        {
            return RecurringTaskActionResult.Failure("Задача уже отложена на более позднее время.");
        }

        var candidate = await candidateService.FindCandidateAsync(
            request.Timezone,
            request.RuleId,
            request.DeduplicationKey,
            request.Type,
            request.ClientId,
            request.TeacherId,
            request.AppointmentId,
            ct);
        if (candidate is null)
        {
            return RecurringTaskActionResult.Failure("Задача больше не актуальна.");
        }

        var nowUtc = UtcNow;
        var execution = existingExecution ?? new RecurringTaskExecution
        {
            Id = Ulid.NewUlid(),
            RuleId = candidate.RuleId,
            Rule = null!,
            Status = RecurringTaskStatus.Completed,
            RecipientType = candidate.RecipientType,
            BusinessDate = candidate.BusinessDate,
            DeduplicationKey = candidate.DeduplicationKey,
            CreatedAtUtc = nowUtc
        };

        execution.RuleId = candidate.RuleId;
        execution.Rule = null!;
        execution.Status = RecurringTaskStatus.Completed;
        execution.RecipientType = candidate.RecipientType;
        execution.ClientId = candidate.ClientId;
        execution.TeacherId = candidate.TeacherId;
        execution.AppointmentId = candidate.AppointmentId;
        execution.BusinessDate = candidate.BusinessDate;
        execution.DeduplicationKey = candidate.DeduplicationKey;
        execution.GeneratedText = request.PreparedMessage ?? candidate.PreparedMessage;
        execution.CompletedByUserId = actor.Id;
        execution.CancelledByUserId = null;
        execution.DelayedByUserId = null;
        execution.CompletedAtUtc = nowUtc;
        execution.CancelledAtUtc = null;
        execution.DelayedAtUtc = null;
        execution.DelayedUntilUtc = null;

        if (existingExecution is null)
        {
            await db.RecurringTaskExecutions.AddAsync(execution, ct);
        }

        await db.SaveChangesAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "recurring_tasks",
            Action = "task_completed",
            EntityType = "recurring_task",
            EntityId = execution.Id.ToString(),
            Details = BuildRecurringTaskAuditDetails(candidate)
        }, ct);

        return RecurringTaskActionResult.Success(RecurringTaskStatus.Completed);
    }

    public async Task<RecurringTaskActionResult> CancelAsync(CancelRecurringTaskRequest request, User actor, CancellationToken ct)
    {
        if (!RecurringTaskTypeExtensions.TryParseApiKey(request.Type, out var type))
        {
            return RecurringTaskActionResult.Failure("Неизвестный тип задачи.");
        }

        if (type == RecurringTaskType.CustomTask)
        {
            return await customTaskTransitions.CancelAsync(request, actor, ct);
        }

        var existingExecution = await db.RecurringTaskExecutions
            .FirstOrDefaultAsync(execution => execution.DeduplicationKey == request.DeduplicationKey, ct);

        if (existingExecution is { Status: not RecurringTaskStatus.Delayed })
        {
            return RecurringTaskActionResult.Failure("Задача уже обработана другим пользователем.");
        }

        if (existingExecution is { Status: RecurringTaskStatus.Delayed, DelayedUntilUtc: { } delayedUntilUtc } && delayedUntilUtc > UtcNow)
        {
            return RecurringTaskActionResult.Failure("Задача уже отложена на более позднее время.");
        }

        var candidate = await candidateService.FindCandidateAsync(
            request.Timezone,
            request.RuleId,
            request.DeduplicationKey,
            request.Type,
            request.ClientId,
            request.TeacherId,
            request.AppointmentId,
            ct);
        if (candidate is null)
        {
            return RecurringTaskActionResult.Failure("Задача больше не актуальна.");
        }

        var nowUtc = UtcNow;
        var execution = existingExecution ?? new RecurringTaskExecution
        {
            Id = Ulid.NewUlid(),
            RuleId = candidate.RuleId,
            Rule = null!,
            Status = RecurringTaskStatus.Cancelled,
            RecipientType = candidate.RecipientType,
            BusinessDate = candidate.BusinessDate,
            DeduplicationKey = candidate.DeduplicationKey,
            CreatedAtUtc = nowUtc
        };

        execution.RuleId = candidate.RuleId;
        execution.Rule = null!;
        execution.Status = RecurringTaskStatus.Cancelled;
        execution.RecipientType = candidate.RecipientType;
        execution.ClientId = candidate.ClientId;
        execution.TeacherId = candidate.TeacherId;
        execution.AppointmentId = candidate.AppointmentId;
        execution.BusinessDate = candidate.BusinessDate;
        execution.DeduplicationKey = candidate.DeduplicationKey;
        execution.GeneratedText = candidate.PreparedMessage;
        execution.CompletedByUserId = null;
        execution.CancelledByUserId = actor.Id;
        execution.DelayedByUserId = null;
        execution.CompletedAtUtc = null;
        execution.CancelledAtUtc = nowUtc;
        execution.DelayedAtUtc = null;
        execution.DelayedUntilUtc = null;

        if (existingExecution is null)
        {
            await db.RecurringTaskExecutions.AddAsync(execution, ct);
        }

        await db.SaveChangesAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "recurring_tasks",
            Action = "task_cancelled",
            EntityType = "recurring_task",
            EntityId = execution.Id.ToString(),
            Details = BuildRecurringTaskAuditDetails(candidate)
        }, ct);

        return RecurringTaskActionResult.Success(RecurringTaskStatus.Cancelled);
    }

    public async Task<RecurringTaskActionResult> DelayAsync(DelayRecurringTaskRequest request, User actor, CancellationToken ct)
    {
        if (!RecurringTaskTypeExtensions.TryParseApiKey(request.Type, out var type))
        {
            return RecurringTaskActionResult.Failure("Неизвестный тип задачи.");
        }

        var delayUntilUtc = request.DelayUntilUtc.Kind switch
        {
            DateTimeKind.Utc => request.DelayUntilUtc,
            DateTimeKind.Local => request.DelayUntilUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(request.DelayUntilUtc, DateTimeKind.Utc)
        };

        if (delayUntilUtc <= UtcNow)
        {
            return RecurringTaskActionResult.Failure("Дата и время переноса должны быть в будущем.");
        }

        if (type == RecurringTaskType.CustomTask)
        {
            return await customTaskTransitions.DelayAsync(request, delayUntilUtc, actor, ct);
        }

        var existingExecution = await db.RecurringTaskExecutions
            .FirstOrDefaultAsync(execution => execution.DeduplicationKey == request.DeduplicationKey, ct);

        if (existingExecution is { Status: not RecurringTaskStatus.Delayed })
        {
            return RecurringTaskActionResult.Failure("Задача уже обработана другим пользователем.");
        }

        var candidate = await candidateService.FindCandidateAsync(
            request.Timezone,
            request.RuleId,
            request.DeduplicationKey,
            request.Type,
            request.ClientId,
            request.TeacherId,
            request.AppointmentId,
            ct);
        if (candidate is null)
        {
            return RecurringTaskActionResult.Failure("Задача больше не актуальна.");
        }

        var nowUtc = UtcNow;
        var execution = existingExecution ?? new RecurringTaskExecution
        {
            Id = Ulid.NewUlid(),
            RuleId = candidate.RuleId,
            Rule = null!,
            Status = RecurringTaskStatus.Delayed,
            RecipientType = candidate.RecipientType,
            BusinessDate = candidate.BusinessDate,
            DeduplicationKey = candidate.DeduplicationKey,
            CreatedAtUtc = nowUtc
        };

        execution.RuleId = candidate.RuleId;
        execution.Rule = null!;
        execution.Status = RecurringTaskStatus.Delayed;
        execution.RecipientType = candidate.RecipientType;
        execution.ClientId = candidate.ClientId;
        execution.TeacherId = candidate.TeacherId;
        execution.AppointmentId = candidate.AppointmentId;
        execution.BusinessDate = candidate.BusinessDate;
        execution.DeduplicationKey = candidate.DeduplicationKey;
        execution.GeneratedText = candidate.PreparedMessage;
        execution.CompletedByUserId = null;
        execution.CancelledByUserId = null;
        execution.DelayedByUserId = actor.Id;
        execution.CompletedAtUtc = null;
        execution.CancelledAtUtc = null;
        execution.DelayedAtUtc = nowUtc;
        execution.DelayedUntilUtc = delayUntilUtc;

        if (existingExecution is null)
        {
            await db.RecurringTaskExecutions.AddAsync(execution, ct);
        }

        await db.SaveChangesAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "recurring_tasks",
            Action = "task_delayed",
            EntityType = "recurring_task",
            EntityId = execution.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                BuildRecurringTaskAuditDetails(candidate),
                AuditDetailsFormatter.DescribeContext("Отложено до", delayUntilUtc))
        }, ct);

        return RecurringTaskActionResult.Success(RecurringTaskStatus.Delayed);
    }

    private static string BuildRecurringTaskAuditDetails(RecurringTaskCandidate candidate)
    {
        return AuditDetailsFormatter.JoinChanges(
            AuditDetailsFormatter.DescribeContext("Тип", candidate.Type.ToDisplayLabel()),
            AuditDetailsFormatter.DescribeContext("Задача", candidate.Title),
            AuditDetailsFormatter.DescribeContext("Получатель", candidate.RelatedPersonDisplayName),
            AuditDetailsFormatter.DescribeContext("Дата", candidate.BusinessDate.ToString("dd.MM.yyyy")),
            candidate.RelevantAtUtc is null ? null : AuditDetailsFormatter.DescribeContext("Время", candidate.RelevantAtUtc));
    }

}
