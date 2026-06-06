using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Extensions;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class LookupClientsEndpoint(AppDbContext db) : Ep.Req<LookupClientsRequest>.Res<Results<Ok<LookupClientsResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    public override void Configure()
    {
        Get("/clients/lookup");
    }

    public override async Task<Results<Ok<LookupClientsResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(LookupClientsRequest req, CancellationToken ct)
    {
        var currentUserRole = await EndpointAuthUtils.GetCurrentUserRoleAsync(User, db, ct);
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

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

        return TypedResults.Ok(new LookupClientsResponse
        {
            Clients = clients
        });
    }
}
