using Backend.Api.Base.Models;
using Backend.Api.Clients.Models;
using Backend.Data;
using Backend.Data.Entities;
using FastEndpoints;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Backend.Api.Clients.Endpoints;

/// <summary>
/// Создание клиента
/// </summary>
/// <param name="db">БД</param>
public class CreateClientEndpoint(AppDbContext db)
    : Endpoint<UpdateClientRequest, Results<Ok<CreateEntityResponse>, ProblemDetails>>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Post("/api/clients");
    }

    /// <inheritdoc />
    public override async Task<Results<Ok<CreateEntityResponse>, ProblemDetails>> ExecuteAsync(UpdateClientRequest req,
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
                Vk = req.Vk
            }
        };
        await db.Clients.AddAsync(client, ct);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("{Name} created a new client", HttpContext.User.Identity?.Name);

        return TypedResults.Ok(new CreateEntityResponse
        {
            Id = client.Id
        });
    }
}