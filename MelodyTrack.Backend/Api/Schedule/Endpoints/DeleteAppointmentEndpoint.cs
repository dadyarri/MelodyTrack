using FastEndpoints;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

public class DeleteAppointmentEndpoint(IAppointmentDeletionService appointmentDeletionService, AppDbContext db, IAuditLogService auditLogService)
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

        var appointment = await db.Appointments
            .AsNoTracking()
            .Where(e => e.Id == req.Id && !e.IsDeleted)
            .Select(e => new
            {
                e.Id,
                e.StartDate,
                ClientName = e.Client.LastName + " " + e.Client.FirstName,
                ServiceName = e.Service.Name
            })
            .FirstOrDefaultAsync(ct);

        var result = await appointmentDeletionService.DeleteAsync(req.Id, scope, ct);

        if (result == DeleteAppointmentResult.NotFound || appointment is null)
        {
            Logger.LogInformation("Appointment with ID {AppointmentId} was already deleted or not found", req.Id);
            return TypedResults.NoContent();
        }

        Logger.LogInformation("Successfully deleted appointment with ID: {AppointmentId}", req.Id);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "schedule",
            Action = scope switch
            {
                AppointmentDeleteScope.ThisAndFollowing => "appointments_deleted_this_and_following",
                AppointmentDeleteScope.All => "appointments_deleted_all",
                _ => "appointment_deleted"
            },
            EntityType = "appointment",
            EntityId = appointment.Id.ToString(),
            Details = $"{appointment.ClientName}, {appointment.ServiceName}, {appointment.StartDate:O}"
        }, ct);
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
