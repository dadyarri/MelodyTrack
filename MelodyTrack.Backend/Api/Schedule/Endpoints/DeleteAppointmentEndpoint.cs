using FastEndpoints;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

public class DeleteAppointmentEndpoint(IAppointmentDeletionService appointmentDeletionService)
    : Ep.Req<DeleteAppointmentRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult, ProblemDetails>>
{
    public override void Configure()
    {
        Delete("/appointments/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult, ProblemDetails>> ExecuteAsync(DeleteAppointmentRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Attempting to delete appointment with ID: {AppointmentId}", req.Id);
        if (!TryParseScope(req.Scope, out var scope))
        {
            AddError(r => r.Scope, "Некорректная область удаления");
            return new ProblemDetails(ValidationFailures);
        }

        var result = await appointmentDeletionService.DeleteAsync(req.Id, scope, ct);

        if (result == DeleteAppointmentResult.NotFound)
        {
            Logger.LogWarning("Failed to delete appointment: ID {AppointmentId} not found", req.Id);
            AddError(r => r.Id, "Встреча не найдена");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        Logger.LogInformation("Successfully deleted appointment with ID: {AppointmentId}", req.Id);
        return TypedResults.NoContent();
    }

    private static bool TryParseScope(string? rawScope, out AppointmentDeleteScope scope)
    {
        scope = AppointmentDeleteScope.Single;

        if (string.IsNullOrWhiteSpace(rawScope))
        {
            return true;
        }

        return rawScope switch
        {
            "single" => true,
            "this-and-following" => (scope = AppointmentDeleteScope.ThisAndFollowing) == AppointmentDeleteScope.ThisAndFollowing,
            "all" => (scope = AppointmentDeleteScope.All) == AppointmentDeleteScope.All,
            _ => false
        };
    }
}
