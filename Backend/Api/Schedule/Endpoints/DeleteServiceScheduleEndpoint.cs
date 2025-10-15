using Backend.Data;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Schedule.Endpoints;

/// <summary>
///     Удалить занятие
/// </summary>
public class DeleteServiceScheduleEndpoint(AppDbContext db)
    : Endpoint<EmptyRequest, Results<NoContent, NotFound, UnauthorizedHttpResult>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Delete("/schedule/{id:long}");
    }

    /// <inheritdoc />
    public override async Task<Results<NoContent, NotFound, UnauthorizedHttpResult>> ExecuteAsync(
        EmptyRequest req, CancellationToken ct)
    {
        var id = Route<long>("id");
        await db.Schedule.Where(e => e.Id == id).ExecuteDeleteAsync(ct);
        return TypedResults.NoContent();
    }
}