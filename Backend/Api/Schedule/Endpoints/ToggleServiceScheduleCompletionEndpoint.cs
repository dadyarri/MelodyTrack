using Backend.Data;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Schedule.Endpoints;

/// <summary>
///     Переключить статус выполнения занятия
/// </summary>
public class ToggleServiceScheduleCompletionEndpoint(AppDbContext db)
    : Endpoint<EmptyRequest, Results<Ok<EmptyResponse>, NotFound, UnauthorizedHttpResult>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Patch("/api/schedule/{id:long}/completion");
    }

    /// <inheritdoc />
    public override async Task<Results<Ok<EmptyResponse>, NotFound, UnauthorizedHttpResult>> ExecuteAsync(
        EmptyRequest req, CancellationToken ct)
    {
        var id = Route<long>("id");
        var entry = await db.Schedule.Where(e => e.Id == id).FirstOrDefaultAsync(ct);

        if (entry == null) return TypedResults.NotFound();

        entry.Completed = !entry.Completed;
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new EmptyResponse());
    }
}