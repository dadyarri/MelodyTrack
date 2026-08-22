using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Api.Tasks.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Services.RecurringTasks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Tasks.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/tasks/{taskId}/deferral")]
public sealed class DelayRecurringTaskEndpoint
{

    public static async Task<Results<Ok<RecurringTaskActionResponse>, UnauthorizedHttpResult, ForbidHttpResult, ProblemHttpResult>> HandleAsync(
        DelayRecurringTaskRequest req,
        string taskId,
        IRecurringTaskService recurringTaskService,
        ICurrentUserAccessor currentUserAccessor,
        CancellationToken ct
    )
    {
        req.DeduplicationKey = taskId;
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!TaskAccess.CanAccessTasks(currentUser))
        {
            return TypedResults.Forbid();
        }

        var result = await recurringTaskService.DelayAsync(req, currentUser, ct);
        if (!result.Succeeded)
        {
            return TypedResults.Problem(result.ErrorMessage, statusCode: StatusCodes.Status409Conflict);
        }

        return TypedResults.Ok(new RecurringTaskActionResponse
        {
            Status = result.Status!.Value.ToApiKey()
        });
    }
}
