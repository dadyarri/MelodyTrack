using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Api.Services.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

public class GetServiceEndpoint(AppDbContext db, IRecordActivityService recordActivityService)
    : Ep.Req<GetEntityRequest>.Res<Results<Ok<ServiceWithCurrentPriceDto>, NotFound<ProblemDetails>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Get("/services/{id}");
    }

    public override async Task<Results<Ok<ServiceWithCurrentPriceDto>, NotFound<ProblemDetails>, UnauthorizedHttpResult>> ExecuteAsync(GetEntityRequest req, CancellationToken ct)
    {
        var service = await db.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(item => item.Id == req.Id, ct);

        if (service is null)
        {
            AddError(item => item.Id, "Сервис не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        var latestPrice = await db.ServicePriceHistory
            .AsNoTracking()
            .Where(item => item.Service.Id == service.Id)
            .OrderByDescending(item => item.EffectiveDate)
            .Select(item => (decimal?)item.Price)
            .FirstOrDefaultAsync(ct);

        return TypedResults.Ok(new ServiceWithCurrentPriceDto
        {
            Id = service.Id,
            Name = service.Name,
            Description = service.Description,
            Price = latestPrice ?? 0m,
            LastActivity = await recordActivityService.GetLatestActivityAsync("service", service.Id.ToString(), ct)
        });
    }
}
