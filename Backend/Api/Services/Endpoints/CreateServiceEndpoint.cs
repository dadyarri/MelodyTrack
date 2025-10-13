using System.Security.Claims;
using Backend.Api.Base.Models;
using Backend.Api.Services.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Services.Endpoints;

/// <summary>
///     Создать услугу
/// </summary>
/// <param name="db">БД</param>
public class CreateServiceEndpoint(AppDbContext db)
    : Endpoint<CreateServiceRequest,
        Results<Ok<CreateEntityResponse>, UnauthorizedHttpResult, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Post("/services");
    }

    /// <inheritdoc />
    public override async Task<Results<Ok<CreateEntityResponse>, UnauthorizedHttpResult, ProblemDetails>>
        ExecuteAsync(
            CreateServiceRequest req,
            CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (login is null) return TypedResults.Unauthorized();

        var user = await db.Users.Where(e => e.Username == login.Value).FirstOrDefaultAsync(ct);

        if (user is null) return TypedResults.Unauthorized();

        var service = new Service
        {
            Name = req.Name,
            Description = req.Description,
            Provider = user
        };


        await db.Services.AddAsync(service, ct);

        var priceItem = new ServicePriceHistory
        {
            Service = service,
            Price = req.Price,
            EffectiveDate = DateTime.UtcNow
        };
        await db.ServicePriceHistories.AddAsync(priceItem, ct);

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new CreateEntityResponse
        {
            Id = service.Id
        });
    }
}