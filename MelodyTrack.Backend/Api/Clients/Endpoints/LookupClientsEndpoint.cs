using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Extensions;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class LookupClientsEndpoint(AppDbContext db) : Ep.Req<LookupClientsRequest>.Res<LookupClientsResponse>
{
    public override void Configure()
    {
        Get("/clients/lookup");
    }

    public override async Task<LookupClientsResponse> ExecuteAsync(LookupClientsRequest req, CancellationToken ct)
    {
        Logger.LogDebug("Fetching lookup list of clients with search: {Search}", req.Search ?? "not specified");
        var clients = await db.Clients
            .AsNoTracking()
            .Include(e => e.Contacts)
            .Include(e => e.Source)
            .ApplyClientFullNameSearch(req.Search)
            .OrderBy(e => e.LastName)
            .ThenBy(e => e.FirstName)
            .Select(e => new LookupClientDto
            {
                Id = e.Id,
                FirstName = e.FirstName,
                LastName = e.LastName,
                Patronymic = e.Patronymic,
                Contacts = new ClientHistoryContactsDto
                {
                    Id = e.Contacts.Id,
                    Telegram = e.Contacts.Telegram,
                    Vk = e.Contacts.Vk,
                    Phone = e.Contacts.Phone
                },
                SourceId = e.SourceId,
                SourceName = e.Source != null ? e.Source.Name : null
            })
            .ToListAsync(ct);

        Logger.LogInformation("Retrieved {Count} clients for lookup list", clients.Count);

        return new LookupClientsResponse
        {
            Clients = clients
        };
    }
}
