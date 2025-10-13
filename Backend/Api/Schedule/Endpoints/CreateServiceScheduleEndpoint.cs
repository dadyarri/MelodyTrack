using System.Security.Claims;
using Backend.Api.Base.Models;
using Backend.Api.Schedule.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Schedule.Endpoints;

/// <summary>
///     Создать запись на ячейку времени
/// </summary>
/// <param name="db">БД</param>
public class CreateServiceScheduleEndpoint(AppDbContext db)
    : Endpoint<CreateServiceScheduleRequest, Results<Ok<CreateEntityResponse>,
        UnauthorizedHttpResult, NotFound, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Post("/schedule");
    }

    /// <inheritdoc />
    public override async Task<Results<Ok<CreateEntityResponse>, UnauthorizedHttpResult, NotFound, ProblemDetails>>
        ExecuteAsync(CreateServiceScheduleRequest req, CancellationToken ct)
    {
        var login = User.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name);

        if (login is null) return TypedResults.Unauthorized();

        var user = await db.Users.Where(e => e.Username == login.Value).FirstOrDefaultAsync(ct);

        if (user is null) return TypedResults.Unauthorized();

        var service = await db.Services.Where(e => e.Id == req.ServiceId).FirstOrDefaultAsync(ct);

        if (service is null) return TypedResults.NotFound();

        var client = await db.Clients.Where(e => e.Id == req.ClientId).FirstOrDefaultAsync(ct);

        if (client is null) return TypedResults.NotFound();

        var entity = new ServiceHistory
        {
            Client = client,
            Service = service,
            StartDate = req.Start,
            EndDate = req.Start.AddHours(1)
        };

        await db.Schedule.AddAsync(entity, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new CreateEntityResponse { Id = entity.Id });
    }
}