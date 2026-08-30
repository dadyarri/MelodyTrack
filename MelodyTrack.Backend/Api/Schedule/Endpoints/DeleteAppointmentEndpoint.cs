using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

[ApiEndpoint(ApiMethod.Delete, "/appointments/{id}")]
public sealed class DeleteAppointmentEndpoint
{

    public static async Task<Results<NoContent, NotFound<ApiProblemDetails>, UnauthorizedHttpResult, ApiProblemDetails, Conflict<StaleEntityConflictResponse>>> HandleAsync(
        [AsParameters] DeleteAppointmentRequest req,
        IAppointmentDeletionService appointmentDeletionService,
        AppDbContext db,
        IAuditLogService auditLogService,
        IEntityFreshnessService entityFreshnessService,
        ILogger<DeleteAppointmentEndpoint> logger,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        logger.LogDebug("Attempting to delete appointment with ID: {AppointmentId}", req.Id);
        if (!TryParseScope(req.Scope, out var scope))
        {
            validationErrors.Add(nameof(req.Scope), "Некорректная область удаления");
            return new ApiProblemDetails(validationErrors);
        }

        var appointment = await db.Appointments
            .AsNoTracking()
            .Where(e => e.Id == req.Id && !e.IsDeleted)
            .Select(e => new
            {
                e.Id,
                e.StartDate,
                ClientName = e.Client.LastName + " " + e.Client.FirstName,
                ServiceName = e.Service.Name,
                ProviderName = e.Provider != null ? e.Provider.LastName + " " + e.Provider.FirstName : null
            })
            .FirstOrDefaultAsync(ct);

        if (appointment is null)
        {
            logger.LogInformation("Appointment with ID {AppointmentId} was already deleted or not found", req.Id);
            return TypedResults.NoContent();
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "appointment",
            appointment.Id,
            req.ExpectedActivityId,
            "Запись была изменена другим пользователем. Проверьте последние изменения перед удалением.",
            ct);

        if (conflict is not null)
        {
            return TypedResults.Conflict(conflict);
        }

        var result = await appointmentDeletionService.DeleteAsync(req.Id, scope, ct);

        if (result == DeleteAppointmentResult.NotFound)
        {
            logger.LogInformation("Appointment with ID {AppointmentId} was already deleted or not found", req.Id);
            return TypedResults.NoContent();
        }

        logger.LogInformation("Successfully deleted appointment with ID: {AppointmentId}", req.Id);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = scope switch
            {
                AppointmentDeleteScope.WeekdayThisAndFollowing => MelodyTrack.Core.Auditing.AuditCatalog.Events.AppointmentsDeletedSelectedWeekdayThisAndFollowing,
                AppointmentDeleteScope.WeekdayAll => MelodyTrack.Core.Auditing.AuditCatalog.Events.AppointmentsDeletedSelectedWeekdayAll,
                AppointmentDeleteScope.ThisAndFollowing => MelodyTrack.Core.Auditing.AuditCatalog.Events.AppointmentsDeletedThisAndFollowing,
                AppointmentDeleteScope.All => MelodyTrack.Core.Auditing.AuditCatalog.Events.AppointmentsDeletedAll,
                _ => MelodyTrack.Core.Auditing.AuditCatalog.Events.AppointmentDeleted
            },
            EntityType = "appointment",
            EntityId = appointment.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", appointment.ClientName),
                AuditDetailsFormatter.DescribeContext("Услуга", appointment.ServiceName),
                AuditDetailsFormatter.DescribeContext("Преподаватель", appointment.ProviderName),
                AuditDetailsFormatter.DescribeContext("Начало", appointment.StartDate)
            )
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
            "weekday-this-and-following" => (scope = AppointmentDeleteScope.WeekdayThisAndFollowing) == AppointmentDeleteScope.WeekdayThisAndFollowing,
            "weekday-all" => (scope = AppointmentDeleteScope.WeekdayAll) == AppointmentDeleteScope.WeekdayAll,
            _ => false
        };
    }
}
