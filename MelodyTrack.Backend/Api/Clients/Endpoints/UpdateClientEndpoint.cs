using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Common.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class UpdateClientEndpoint(AppDbContext db, IAuditLogService auditLogService)
    : Ep.Req<UpdateClientRequest>.Res<Results<Ok<GetEntityRequest>, NotFound, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Put("/clients/{id}");
    }

    public override async Task<Results<Ok<GetEntityRequest>, NotFound, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(UpdateClientRequest req,
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
            return TypedResults.NotFound();
        }

        var latestActivity = await db.AuditLogs
            .AsNoTracking()
            .Where(item => item.EntityType == "client" && item.EntityId == client.Id.ToString())
            .OrderByDescending(item => item.CreatedAtUtc)
            .Select(item => new RecordActivityDto
            {
                Id = item.Id,
                CreatedAtUtc = item.CreatedAtUtc,
                Category = item.Category,
                Action = item.Action,
                ActorEmail = item.ActorEmail,
                ActorDisplayName = item.ActorDisplayName,
                SourceIpAddress = item.SourceIpAddress,
                Details = item.Details
            })
            .FirstOrDefaultAsync(ct);

        if (EntityFreshnessUtils.IsStale(req.ExpectedActivityId, latestActivity) && !IsNoOp(client, req))
        {
            return TypedResults.Conflict(EntityFreshnessUtils.CreateConflict(
                "client",
                client.Id,
                "Клиент был изменен другим пользователем. Обновите данные или повторите сохранение поверх новой версии.",
                latestActivity));
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
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "clients",
            Action = "client_updated",
            EntityType = "client",
            EntityId = client.Id.ToString(),
            Details = $"{client.LastName} {client.FirstName}".Trim()
        }, ct);

        return TypedResults.Ok(new GetEntityRequest { Id = req.Id });
    }

    private static bool IsNoOp(Data.Models.Client client, UpdateClientRequest req)
    {
        return (req.FirstName is null || req.FirstName == client.FirstName)
               && (req.LastName is null || req.LastName == client.LastName)
               && req.Patronymic == client.Patronymic
               && req.Phone == client.Contacts.Phone
               && req.Telegram == client.Contacts.Telegram
               && req.Vk == client.Contacts.Vk;
    }
}
