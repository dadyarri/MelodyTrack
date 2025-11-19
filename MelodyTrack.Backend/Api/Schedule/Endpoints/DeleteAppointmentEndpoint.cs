using FastEndpoints;
using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

public class DeleteAppointmentEndpoint(AppDbContext db) : Ep.Req<GetEntityRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Delete("/appointments/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Attempting to delete appointment with ID: {AppointmentId}", req.Id);
        var rowsDeleted = await db.Appointments.Where(e => e.Id == req.Id).ExecuteDeleteAsync(ct);

        if (rowsDeleted == 0)
        {
            Logger.LogWarning("Failed to delete appointment: ID {AppointmentId} not found", req.Id);
            AddError(r => r.Id, "Встреча не найдена");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        Logger.LogInformation("Successfully deleted appointment with ID: {AppointmentId}", req.Id);
        return TypedResults.NoContent();
    }
}