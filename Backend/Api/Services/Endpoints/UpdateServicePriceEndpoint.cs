using System.Security.Claims;
using Backend.Api.Base.Models;
using Backend.Api.Services.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Services.Endpoints;

public class UpdateServicePriceEndpoint(AppDbContext db)
    : Endpoint<UpdateServicePriceRequest,
        Results<Ok<CreateEntityResponse>, UnauthorizedHttpResult, NotFound, ProblemDetails>>
{
    public override void Configure()
    {
        Patch("/api/services/{id:long}/price");
    }

    public override async Task<Results<Ok<CreateEntityResponse>, UnauthorizedHttpResult, NotFound, ProblemDetails>>
        ExecuteAsync(
            UpdateServicePriceRequest req,
            CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (login is null) return TypedResults.Unauthorized();

        var user = await db.Users.Where(e => e.Username == login.Value).FirstOrDefaultAsync(ct);

        if (user is null) return TypedResults.Unauthorized();

        var id = Route<long>("id");

        var service = await db.Services
            .Where(e => e.Id == id && e.Provider == user)
            .FirstOrDefaultAsync(ct);

        if (service is null) return TypedResults.NotFound();

        var priceHistory = new ServicePriceHistory
        {
            Service = service,
            Price = req.Price,
            EffectiveDate = DateTime.UtcNow
        };

        await db.ServicePriceHistories.AddAsync(priceHistory, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new CreateEntityResponse
        {
            Id = priceHistory.Id
        });
    }
}