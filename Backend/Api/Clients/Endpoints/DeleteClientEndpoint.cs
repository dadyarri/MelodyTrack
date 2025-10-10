using Backend.Data;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Clients.Endpoints;

/// <summary>
/// Удаление клиента
/// </summary>
/// <param name="db">БД</param>
public class DeleteClientEndpoint(AppDbContext db)
    : Endpoint<EmptyRequest, Results<NoContent, NotFound, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Delete("/api/clients/{id:long}");
    }

    /// <inheritdoc />
    public override async Task<Results<NoContent, NotFound, ProblemDetails>> ExecuteAsync(
        EmptyRequest req, CancellationToken ct)
    {
        var clientId = Route<long>("id");

        var client = await db.Clients.Where(e => e.Id == clientId).FirstOrDefaultAsync(ct);

        if (client == null) return TypedResults.NotFound();

        db.Remove(client);

        await db.SaveChangesAsync(ct);

        return TypedResults.NoContent();
    }
}