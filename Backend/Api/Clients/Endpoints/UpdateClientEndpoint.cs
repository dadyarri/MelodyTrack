using Backend.Api.Base.Models;
using Backend.Api.Clients.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Backend.Api.Clients.Endpoints;

public class
    UpdateClientEndpoint(AppDbContext db)
    : Endpoint<UpdateClientRequest, Results<Ok<CreateEntityResponse>, NotFound, ProblemDetails>>
{
    public override void Configure()
    {
        Put("/api/clients/{id:long}");
    }

    public override async Task<Results<Ok<CreateEntityResponse>, NotFound, ProblemDetails>> ExecuteAsync(
        UpdateClientRequest req, CancellationToken ct)
    {
        var clientId = Route<long>("id");

        var client = await db.Clients.Where(e => e.Id == clientId).FirstOrDefaultAsync(ct);

        if (client == null)
        {
            return TypedResults.NotFound();
        }

        client.FirstName = req.FirstName;
        client.LastName = req.LastName;
        client.Patronymic = req.Patronymic;
        client.Contacts = new ClientContact
        {
            Phone = req.Phone,
            Telegram = req.Telegram,
            Vk = req.Vk,
        };

        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new CreateEntityResponse { Id = client.Id });
    }
}