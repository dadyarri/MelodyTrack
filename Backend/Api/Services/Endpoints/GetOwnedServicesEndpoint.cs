using System.Security.Claims;
using Backend.Api.Services.Models;
using Backend.Data;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Services.Endpoints;

/// <summary>
///     Получить услуги текущего пользователя
/// </summary>
/// <param name="db">БД</param>
public class GetOwnedServicesEndpoint(AppDbContext db)
    : Endpoint<EmptyRequest,
        Results<Ok<List<ServiceDto>>, ForbidHttpResult, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/api/services");
    }

    /// <inheritdoc />
    public override async Task<Results<Ok<List<ServiceDto>>, ForbidHttpResult, ProblemDetails>>
        ExecuteAsync(
            EmptyRequest req,
            CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (login is null) return TypedResults.Forbid();

        var user = await db.Users.Where(e => e.Username == login.Value).FirstOrDefaultAsync(ct);

        if (user is null) return TypedResults.Forbid();

        var services = await db.Services
            .Where(e => e.Provider == user)
            .ToListAsync(ct);

        List<ServiceDto> serviceDtos = [];

        foreach (var service in services)
        {
            var latestPrice = await db.ServicePriceHistories.Where(e => e.Service == service)
                .OrderByDescending(e => e.EffectiveDate).Take(1).FirstOrDefaultAsync(ct);

            var price = 0m;

            if (latestPrice is not null) price = latestPrice.Price;

            serviceDtos.Add(new ServiceDto
            {
                Id = service.Id,
                Name = service.Name,
                Description = service.Description,
                CurrentPrice = price
            });
        }

        return TypedResults.Ok(serviceDtos);
    }
}