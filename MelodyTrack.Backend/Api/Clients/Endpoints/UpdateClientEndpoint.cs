using FastEndpoints;
using MelodyTrack.Common.Api.Clients.Requests;
using MelodyTrack.Common.Api.Common.Requests;
using MelodyTrack.Common.Api.Common.Responses;
using MelodyTrack.Common.Data;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class UpdateClientEndpoint(AppDbContext db)
    : Ep.Req<UpdateClientRequest>.Res<IResult>
{
    public override void Configure()
    {
        Put("/clients/{id}");
    }

    public override async Task<IResult> ExecuteAsync(UpdateClientRequest req,
        CancellationToken ct)
    {
        Logger.LogInformation(
            "Updating client {ClientId} with new data - FirstName: {FirstName}, LastName: {LastName}, Patronymic: {Patronymic}, Contacts - Phone: {Phone}, Telegram: {Telegram}, VK: {Vk}",
            req.Id,
            req.FirstName,
            req.LastName,
            req.Patronymic,
            req.Phone ?? "not provided",
            req.Telegram ?? "not provided",
            req.Vk ?? "not provided"
        );

        var client = await db.Clients
            .Where(e => e.Id == req.Id)
            .Include(client => client.Contacts)
            .FirstOrDefaultAsync(ct);

        if (client is null)
        {
            return ApiResults.NotFound();
        }

        if (req.FirstName != null)
        {
            client.FirstName = req.FirstName;
        }
        if (req.LastName != null)
        {
            client.LastName = req.LastName;
        }

        client.Patronymic = req.Patronymic;
        client.Contacts.Phone = req.Phone;
        client.Contacts.Telegram = req.Telegram;
        client.Contacts.Vk = req.Vk;

        await db.SaveChangesAsync(ct);

        return ApiResults.Ok(new GetEntityRequest { Id = req.Id });
    }
}