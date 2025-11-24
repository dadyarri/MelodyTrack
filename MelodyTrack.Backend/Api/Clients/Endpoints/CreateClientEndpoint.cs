using FastEndpoints;
using MelodyTrack.Common.Api.Clients.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using MelodyTrack.Common.Data.Models;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class
    CreateClientEndpoint(AppDbContext db)
    : Ep.Req<CreateClientRequest>.Res<IResult>
{
    public override void Configure()
    {
        Post("/clients");
    }

    public override async Task<IResult> ExecuteAsync(
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

        Logger.LogInformation(
            "Created new client: {FirstName} {LastName} (ID: {ClientId}) with contacts - Phone: {Phone}, Telegram: {Telegram}, VK: {Vk}",
            client.FirstName,
            client.LastName,
            client.Id,
            client.Contacts.Phone ?? "not provided",
            client.Contacts.Telegram ?? "not provided",
            client.Contacts.Vk ?? "not provided"
        );

        return ApiResults.Created($"/clients/{client.Id}", new CreateEntityResponse
        {
            Id = client.Id
        });
    }
}