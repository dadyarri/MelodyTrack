using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Services.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

public class UpdateServicePriceEndpoint(AppDbContext db, IAuditLogService auditLogService) : Ep.Req<UpdateServicePriceRequest>.Res<Results<Ok<CreateEntityResponse>, UnauthorizedHttpResult, NotFound>>
{
    public override void Configure()
    {
        Patch("/services/{id}/price");
    }

    public override async Task<Results<Ok<CreateEntityResponse>, UnauthorizedHttpResult, NotFound>> ExecuteAsync(UpdateServicePriceRequest req, CancellationToken ct)
    {
        var service = await db.Services
            .Where(e => e.Id == req.Id)
            .FirstOrDefaultAsync(ct);

        if (service is null)
        {
            return TypedResults.NotFound();
        }

        var previousPrice = await db.ServicePriceHistory
            .Where(item => item.Service.Id == service.Id)
            .OrderByDescending(item => item.EffectiveDate)
            .Select(item => (decimal?)item.Price)
            .FirstOrDefaultAsync(ct);

        var price = new ServicePrice
        {
            Id = Ulid.NewUlid(),
            EffectiveDate = DateTime.UtcNow,
            Price = req.Price,
            Service = service
        };

        await db.ServicePriceHistory.AddAsync(price, ct);
        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "services",
            Action = "service_price_updated",
            EntityType = "service",
            EntityId = service.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Услуга", service.Name),
                AuditDetailsFormatter.DescribeChange("Цена", previousPrice?.ToString("0.##"), req.Price.ToString("0.##"))
            )
        }, ct);

        return TypedResults.Ok(new CreateEntityResponse
        {
            Id = service.Id
        });
    }
}
