using MelodyTrack.Backend.Data.Enums;
using FastEndpoints;
using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Api.Tasks.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services.RecurringTasks;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Tasks.Endpoints;

public class DelayRecurringTaskEndpoint(AppDbContext db, IRecurringTaskService recurringTaskService)
    : Ep.Req<DelayRecurringTaskRequest>.Res<Results<Ok<RecurringTaskActionResponse>, UnauthorizedHttpResult, ForbidHttpResult, ProblemHttpResult>>
{
    public override void Configure()
    {
        Post("/tasks/delay");
    }

    public override async Task<Results<Ok<RecurringTaskActionResponse>, UnauthorizedHttpResult, ForbidHttpResult, ProblemHttpResult>> ExecuteAsync(
        DelayRecurringTaskRequest req,
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
