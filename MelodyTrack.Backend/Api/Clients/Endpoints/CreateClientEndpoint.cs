using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class
    CreateClientEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<CreateClientRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult>>
{
    public override void Configure()
    {
        Post("/clients");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult>> ExecuteAsync(
        CreateClientRequest req, CancellationToken ct)
    {
        var client = new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = req.FirstName,
            LastName = req.LastName,
            Patronymic = req.Patronymic,
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
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "clients",
            Action = "client_created",
            EntityType = "client",
            EntityId = client.Id.ToString(),
            Details = $"{client.LastName} {client.FirstName}".Trim()
        }, ct);

        return TypedResults.Created($"/clients/{client.Id}", new CreateEntityResponse
        {
            Id = client.Id
        });
    }
}
