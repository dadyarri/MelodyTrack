using Backend.Api.Base.Models;
using Backend.Api.Clients.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Backend.Api.Clients.Endpoints;

public class CreateClientEndpoint(AppDbContext db)
    : Endpoint<CreateClientRequest, Results<Ok<CreateEntityResponse>, ProblemDetails>>
{
    public override void Configure()
    {
        Post("/api/clients");
        AllowAnonymous();
    }

    public override async Task<Results<Ok<CreateEntityResponse>, ProblemDetails>> ExecuteAsync(CreateClientRequest req,
        CancellationToken ct)
    {
        var client = new Client
        {
            FirstName = req.FirstName,
            LastName = req.LastName,
            Patronymic = req.Patronymic,
            Contacts = new ClientContact
            {
                Phone = req.Phone,
                Telegram = req.Telegram,
                Vk = req.Vk,
            }
        };
        await db.Clients.AddAsync(client, ct);
        await db.SaveChangesAsync(ct);

        return TypedResults.Ok(new CreateEntityResponse
        {
            Id = client.Id,
        });
    }
}