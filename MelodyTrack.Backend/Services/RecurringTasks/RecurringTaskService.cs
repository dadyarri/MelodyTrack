using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Api.Tasks.Responses;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Services.RecurringTasks;

internal sealed class RecurringTaskService(
    IRecurringTaskCandidateService candidateService,
    IRecurringTaskQueryService queryService,
    IRecurringTaskTransitionService transitionService) : IRecurringTaskService
{
    public async Task<List<RecurringTaskDto>> GetTasksAsync(
        string timezone,
        RecurringTaskType? filterType,
        RecurringTaskListStatus status,
        CancellationToken ct)
    {
        return status switch
        {
            RecurringTaskListStatus.Open => await candidateService.GetOpenTasksAsync(timezone, filterType, ct),
            RecurringTaskListStatus.Completed => await queryService.GetProcessedTasksAsync(
                timezone,
                filterType,
                RecurringTaskStatus.Completed,
                ct),
            RecurringTaskListStatus.Cancelled => await queryService.GetProcessedTasksAsync(
                timezone,
                filterType,
                RecurringTaskStatus.Cancelled,
                ct),
            RecurringTaskListStatus.Delayed => await queryService.GetProcessedTasksAsync(
                timezone,
                filterType,
                RecurringTaskStatus.Delayed,
                ct),
            _ => []
        };
    }

    public Task<RecurringTaskActionResult> CompleteAsync(
        CompleteRecurringTaskRequest request,
        User actor,
        CancellationToken ct) =>
        transitionService.CompleteAsync(request, actor, ct);

    public Task<RecurringTaskActionResult> CancelAsync(
        CancelRecurringTaskRequest request,
        User actor,
        CancellationToken ct) =>
        transitionService.CancelAsync(request, actor, ct);

    public Task<RecurringTaskActionResult> DelayAsync(
        DelayRecurringTaskRequest request,
        User actor,
        CancellationToken ct) =>
        transitionService.DelayAsync(request, actor, ct);
}
