using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class
    CreateClientEndpoint(AppDbContext db)
    : Ep.Req<CreateClientRequest>.Res<Results<Ok<CreateEntityResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/clients");
    }

    public override async Task<Results<Ok<CreateEntityResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        CreateClientRequest req, CancellationToken ct)
    {
        var client = new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = req.FirstName,
            LastName = req.LastName,
            Contacts = new ClientContacts
            {
                Id = Ulid.NewUlid(),
                Telegram = req.Telegram,
                Phone = req.Phone,
                Vk = req.Vk
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