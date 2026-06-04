using FastEndpoints;
using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Api.Tasks.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services.RecurringTasks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Tasks.Endpoints;

public class GetDueRecurringTasksEndpoint(AppDbContext db, IRecurringTaskService recurringTaskService)
    : Ep.Req<GetDueRecurringTasksRequest>.Res<Results<Ok<GetDueRecurringTasksResponse>, UnauthorizedHttpResult, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Get("/tasks/due");
    }

    public override async Task<Results<Ok<GetDueRecurringTasksResponse>, UnauthorizedHttpResult, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        GetDueRecurringTasksRequest req,
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

        if (string.IsNullOrWhiteSpace(req.Timezone))
        {
            return TypedResults.Problem("Не указан часовой пояс.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!RecurringTaskListStatusExtensions.TryParseApiKey(req.Status, out var status))
        {
            return TypedResults.Problem("Неизвестный статус списка задач.", statusCode: StatusCodes.Status400BadRequest);
        }

        if (!string.IsNullOrWhiteSpace(req.Type) && !RecurringTaskTypeExtensions.TryParseApiKey(req.Type, out _))
        {
            return TypedResults.Problem("Неизвестный тип задачи.", statusCode: StatusCodes.Status400BadRequest);
        }

        RecurringTaskType? filterType = RecurringTaskTypeExtensions.TryParseApiKey(req.Type, out var parsedType)
            ? parsedType
            : null;

        var tasks = await recurringTaskService.GetTasksAsync(req.Timezone, filterType, status, ct);
        return TypedResults.Ok(new GetDueRecurringTasksResponse { Tasks = tasks });
    }
}
