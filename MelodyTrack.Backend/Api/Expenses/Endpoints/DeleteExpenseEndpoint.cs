using FastEndpoints;
using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

public class DeleteExpenseEndpoint(AppDbContext db) : Ep.Req<GetEntityRequest>.Res<IResult>
{
    public override void Configure()
    {
        Delete("/expenses/{id}");
    }

    public override async Task<IResult> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Attempting to delete expense with ID: {ExpenseId}", req.Id);
        var rowsDeleted = await db.Expenses.Where(e => e.Id == req.Id).ExecuteDeleteAsync(ct);

        if (rowsDeleted == 0)
        {
            Logger.LogWarning("Failed to delete expense: ID {ExpenseId} not found", req.Id);
            AddError(r => r.Id, "Расход не найден");
            return ApiResults.NotFound(ValidationFailures);
        }

        Logger.LogInformation("Successfully deleted expense with ID: {ExpenseId}", req.Id);
        return ApiResults.NoContent();
    }
}