using FastEndpoints;
using MelodyTrack.Backend.Api.Services.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

public class UpdateServiceEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<UpdateServiceRequest>.Res<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Put("/services/{id}");
    }

    public override async Task<Results<NoContent, NotFound<ProblemDetails>, UnauthorizedHttpResult>> ExecuteAsync(
        UpdateServiceRequest req,
        CancellationToken ct)
    {
        var service = await db.Services.FirstOrDefaultAsync(item => item.Id == req.Id, ct);
        if (service is null)
        {
            AddError(item => item.Id, "Услуга не найдена");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        service.Name = req.Name;
        service.Description = req.Description;

        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "services",
            Action = "service_updated",
            EntityType = "service",
            EntityId = service.Id.ToString(),
            Details = $"{service.Name}{(string.IsNullOrWhiteSpace(service.Description) ? "" : $", {service.Description}")}"
        }, ct);

        return TypedResults.NoContent();
    }
}
