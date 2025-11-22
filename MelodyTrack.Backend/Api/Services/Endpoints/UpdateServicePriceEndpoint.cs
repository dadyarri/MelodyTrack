using FastEndpoints;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Api.Services.Requests;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

public class UpdateServicePriceEndpoint(AppDbContext db) : Ep.Req<UpdateServicePriceRequest>.Res<IResult>
{
    public override void Configure()
    {
        Patch("/services/{id}/price");
    }

    public override async Task<IResult> ExecuteAsync(UpdateServicePriceRequest req, CancellationToken ct)
    {
        var service = await db.Services
            .Where(e => e.Id == req.Id)
            .FirstOrDefaultAsync(ct);

        if (service is null)
        {
            return ApiResults.NotFound();
        }

        var price = new ServicePrice
        {
            Id = Ulid.NewUlid(),
            EffectiveDate = DateTime.UtcNow,
            Price = req.Price,
            Service = service
        };

        await db.ServicePriceHistory.AddAsync(price, ct);
        await db.SaveChangesAsync(ct);

        return ApiResults.Ok(new CreateEntityResponse
        {
            Id = service.Id
        });
    }
}