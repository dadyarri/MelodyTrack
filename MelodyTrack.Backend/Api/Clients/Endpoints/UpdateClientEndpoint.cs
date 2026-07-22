using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class UpdateClientEndpoint(AppDbContext db, IAuditLogService auditLogService, IEntityFreshnessService entityFreshnessService)
    : Ep.Req<UpdateClientRequest>.Res<Results<Ok<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound, Conflict<StaleEntityConflictResponse>>>
{
    public override void Configure()
    {
        Put("/clients/{id}");
    }

    public override async Task<Results<Ok<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound, Conflict<StaleEntityConflictResponse>>> ExecuteAsync(UpdateClientRequest req,
        CancellationToken ct)
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

        Logger.LogInformation(
            "Updating client {ClientId} with new data - FirstName: {FirstName}, LastName: {LastName}, Patronymic: {Patronymic}, DateOfBirth: {DateOfBirth}, Contacts - Phone: {Phone}, Telegram: {Telegram}, VK: {Vk}",
            req.Id,
            req.FirstName,
            req.LastName,
            req.Patronymic,
            req.DateOfBirth?.ToString("yyyy-MM-dd") ?? "not provided",
            req.Phone ?? "not provided",
            req.Telegram ?? "not provided",
            req.Vk ?? "not provided"
        );

        var client = await db.Clients
            .Where(e => e.Id == req.Id)
            .Include(client => client.Contacts)
            .Include(client => client.Source)
            .Include(client => client.Vacations)
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
        var beforeDateOfBirth = client.DateOfBirth;
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
        client.DateOfBirth = req.DateOfBirth;
        client.Contacts.Phone = req.Phone;
        client.Contacts.Telegram = req.Telegram;
        client.Contacts.Vk = req.Vk;
        client.SourceId = req.SourceId;

        if (req.Vacations is not null)
        {
            db.ClientVacations.RemoveRange(client.Vacations);
            client.Vacations = req.Vacations
                .Select(item => new Data.Models.ClientVacation
                {
                    Id = Ulid.NewUlid(),
                    ClientId = client.Id,
                    Client = client,
                    StartDate = item.StartDate,
                    EndDate = item.EndDate
                })
                .ToList();
        }

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
                AuditDetailsFormatter.DescribeChange("Дата рождения", beforeDateOfBirth?.ToString("yyyy-MM-dd"), client.DateOfBirth?.ToString("yyyy-MM-dd")),
                AuditDetailsFormatter.DescribeChange("Телефон", beforePhone, client.Contacts.Phone),
                AuditDetailsFormatter.DescribeChange("Telegram", beforeTelegram, client.Contacts.Telegram),
                AuditDetailsFormatter.DescribeChange("VK", beforeVk, client.Contacts.Vk),
                AuditDetailsFormatter.DescribeChange("Источник", beforeSourceName, client.Source?.Name),
                req.Vacations is null ? null : AuditDetailsFormatter.DescribeContext("Периодов отсутствия", client.Vacations.Count.ToString())
            )
        }, ct);

        return TypedResults.Ok(new CreateEntityResponse { Id = req.Id });
    }

    private static bool IsNoOp(Data.Models.Client client, UpdateClientRequest req)
    {
        return (req.FirstName is null || req.FirstName == client.FirstName)
               && (req.LastName is null || req.LastName == client.LastName)
               && req.Patronymic == client.Patronymic
               && req.DateOfBirth == client.DateOfBirth
               && req.Phone == client.Contacts.Phone
               && req.Telegram == client.Contacts.Telegram
               && req.Vk == client.Contacts.Vk
               && req.SourceId == client.SourceId
               && (req.Vacations is null || client.Vacations.OrderBy(item => item.StartDate).ThenBy(item => item.EndDate)
                   .Select(item => new { item.StartDate, item.EndDate })
                   .SequenceEqual(req.Vacations.OrderBy(item => item.StartDate).ThenBy(item => item.EndDate)
                       .Select(item => new { item.StartDate, item.EndDate })));
    }
}
