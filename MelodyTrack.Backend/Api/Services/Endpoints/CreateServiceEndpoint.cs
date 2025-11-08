using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Services.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

public class CreateServiceEndpoint(AppDbContext db)
    : Ep.Req<CreateServiceRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/services");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        CreateServiceRequest req, CancellationToken ct)
    {
        var service = new Service
        {
            Id = Ulid.NewUlid(),
            Name = req.Name,
            Description = req.Description
        };

        var price = new ServicePrice
        {
            Id = Ulid.NewUlid(),
            Service = service,
            EffectiveDate = DateTime.UtcNow,
            Price = req.Price
        };

        await db.Services.AddAsync(service, ct);
        await db.ServicePriceHistory.AddAsync(price, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Created($"/services/{service.Id}", new CreateEntityResponse
        {
            Id = service.Id
        });
    }
}