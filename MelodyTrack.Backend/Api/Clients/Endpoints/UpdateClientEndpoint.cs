using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class UpdateClientEndpoint(AppDbContext db, IAuditLogService auditLogService, IEntityFreshnessService entityFreshnessService)
    : Ep.Req<UpdateClientRequest>.Res<Results<Ok<CreateEntityResponse>, NotFound, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Put("/clients/{id}");
    }

    public override async Task<Results<Ok<CreateEntityResponse>, NotFound, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(UpdateClientRequest req,
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
            .Include(client => client.Source)
            .FirstOrDefaultAsync(ct);

        if (client is null)
        {
            return TypedResults.NotFound();
        }

        if (req.SourceId is not null)
        {
            var sourceExists = await db.ClientSources.AnyAsync(e => e.Id == req.SourceId.Value, ct);
            if (!sourceExists)
            {
                AddError(e => e.SourceId, "Источник не найден");
                return TypedResults.NotFound();
            }
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "client",
            client.Id,
            req.ExpectedActivityId,
            "Клиент был изменен другим пользователем. Обновите данные или повторите сохранение поверх новой версии.",
            ct);

        if (conflict is not null && !IsNoOp(client, req))
        {
            return TypedResults.Conflict(conflict);
        }

        var beforeFirstName = client.FirstName;
        var beforeLastName = client.LastName;
        var beforePatronymic = client.Patronymic;
        var beforePhone = client.Contacts.Phone;
        var beforeTelegram = client.Contacts.Telegram;
        var beforeVk = client.Contacts.Vk;
        var beforeSourceName = client.Source?.Name;

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
        client.SourceId = req.SourceId;

        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "clients",
            Action = "client_updated",
            EntityType = "client",
            EntityId = client.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeChange("Имя", beforeFirstName, client.FirstName),
                AuditDetailsFormatter.DescribeChange("Фамилия", beforeLastName, client.LastName),
                AuditDetailsFormatter.DescribeChange("Отчество", beforePatronymic, client.Patronymic),
                AuditDetailsFormatter.DescribeChange("Телефон", beforePhone, client.Contacts.Phone),
                AuditDetailsFormatter.DescribeChange("Telegram", beforeTelegram, client.Contacts.Telegram),
                AuditDetailsFormatter.DescribeChange("VK", beforeVk, client.Contacts.Vk),
                AuditDetailsFormatter.DescribeChange("Источник", beforeSourceName, client.Source?.Name)
            )
        }, ct);

        return TypedResults.Ok(new CreateEntityResponse { Id = req.Id });
    }

    private static bool IsNoOp(Data.Models.Client client, UpdateClientRequest req)
    {
        return (req.FirstName is null || req.FirstName == client.FirstName)
               && (req.LastName is null || req.LastName == client.LastName)
               && req.Patronymic == client.Patronymic
               && req.Phone == client.Contacts.Phone
               && req.Telegram == client.Contacts.Telegram
               && req.Vk == client.Contacts.Vk
               && req.SourceId == client.SourceId;
    }
}
