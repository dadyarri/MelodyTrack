using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

public class DeleteServiceEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<GetEntityRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, ProblemDetails, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Delete("/services/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, ProblemDetails, UnauthorizedHttpResult>> ExecuteAsync(
        GetEntityRequest req,
        CancellationToken ct)
    {
        var service = await db.Services
            .AsNoTracking()
            .Where(item => item.Id == req.Id)
            .Select(item => new { item.Id, item.Name, item.Description })
            .FirstOrDefaultAsync(ct);

        if (service is null)
        {
            return TypedResults.NoContent();
        }

        var hasPayments = await db.Payments.AnyAsync(item => item.Service != null && item.Service.Id == req.Id, ct);
        var hasAppointments = await db.Appointments.AnyAsync(item => item.Service.Id == req.Id, ct);
        var hasRecurringRules = await db.RecurrenceRules.AnyAsync(item => item.Service.Id == req.Id, ct);

        if (hasPayments || hasAppointments || hasRecurringRules)
        {
            AddError(item => item.Id, "Нельзя удалить услугу, которая уже используется в платежах или расписании.");
            return new ProblemDetails(ValidationFailures);
        }

        await db.Services.Where(item => item.Id == req.Id).ExecuteDeleteAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "services",
            Action = "service_deleted",
            EntityType = "service",
            EntityId = service.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Услуга", service.Name),
                AuditDetailsFormatter.DescribeContext("Описание", service.Description)
            )
        }, ct);

        return TypedResults.NoContent();
    }
}
