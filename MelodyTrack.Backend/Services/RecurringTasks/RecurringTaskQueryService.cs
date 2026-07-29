using MelodyTrack.Backend.Api.Tasks.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services.RecurringTasks;

public interface IRecurringTaskQueryService
{
    Task<List<RecurringTaskDto>> GetProcessedTasksAsync(
        string timezone,
        RecurringTaskType? filterType,
        RecurringTaskStatus status,
        CancellationToken ct);
}

internal sealed class RecurringTaskQueryService(AppDbContext db, TimeProvider timeProvider) : IRecurringTaskQueryService
{
    public async Task<List<RecurringTaskDto>> GetProcessedTasksAsync(
        string timezone,
        RecurringTaskType? filterType,
        RecurringTaskStatus status,
        CancellationToken ct)
    {
        var recurringQuery = db.RecurringTaskExecutions
            .AsNoTracking()
            .Include(execution => execution.Rule)
            .Include(execution => execution.Client)
            .ThenInclude(client => client!.Contacts)
            .Include(execution => execution.Teacher)
            .Include(execution => execution.Appointment)
            .Where(execution => execution.Status == status);

        if (filterType is { } type)
        {
            recurringQuery = recurringQuery.Where(execution => execution.Rule.Type == type);
        }

        if (status == RecurringTaskStatus.Delayed)
        {
            var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
            recurringQuery = recurringQuery.Where(execution =>
                execution.DelayedUntilUtc != null
                && execution.DelayedUntilUtc > nowUtc
                && (execution.ClientId == null || !execution.Client!.Vacations.Any(vacation =>
                    vacation.StartDate <= execution.BusinessDate && vacation.EndDate >= execution.BusinessDate)));
        }

        var executions = status == RecurringTaskStatus.Delayed
            ? await recurringQuery
                .OrderBy(execution => execution.DelayedUntilUtc)
                .ThenBy(execution => execution.CreatedAtUtc)
                .ToListAsync(ct)
            : await recurringQuery
                .OrderByDescending(execution => execution.CompletedAtUtc ?? execution.CancelledAtUtc ?? execution.DelayedAtUtc ?? execution.CreatedAtUtc)
                .ToListAsync(ct);

        var tasks = executions
            .Select(RecurringTaskPresentationMapper.MapExecution)
            .ToList();

        if (filterType is null or RecurringTaskType.CustomTask)
        {
            tasks.AddRange(await GetProcessedCustomTasksAsync(timezone, status, ct));
        }

        return status == RecurringTaskStatus.Delayed
            ? tasks.OrderBy(task => task.DelayedUntilUtc).ThenBy(task => task.RelevantAtUtc).ToList()
            : tasks.OrderByDescending(task => task.DelayedUntilUtc ?? task.RelevantAtUtc).ToList();
    }

    private async Task<List<RecurringTaskDto>> GetProcessedCustomTasksAsync(
        string timezone,
        RecurringTaskStatus status,
        CancellationToken ct)
    {
        var query = db.CustomTasks
            .AsNoTracking()
            .Include(item => item.Client)
            .ThenInclude(client => client!.Contacts)
            .AsQueryable();

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        query = status switch
        {
            RecurringTaskStatus.Completed => query.Where(item => item.CompletedAtUtc != null),
            RecurringTaskStatus.Cancelled => query.Where(item => item.CancelledAtUtc != null),
            RecurringTaskStatus.Delayed => query.Where(item => item.DelayedUntilUtc != null && item.DelayedUntilUtc > nowUtc),
            _ => query.Where(_ => false)
        };

        var tasks = status == RecurringTaskStatus.Delayed
            ? await query.OrderBy(item => item.DelayedUntilUtc).ThenBy(item => item.DueAtUtc).ToListAsync(ct)
            : await query.OrderByDescending(item => item.CompletedAtUtc ?? item.CancelledAtUtc ?? item.DelayedAtUtc ?? item.CreatedAtUtc).ToListAsync(ct);

        return tasks
            .Select(task => RecurringTaskPresentationMapper.MapCustomTaskExecution(task, timezone))
            .ToList();
    }
}
