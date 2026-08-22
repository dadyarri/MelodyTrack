using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Clients;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

[ApiEndpoint(ApiMethod.Patch, "/clients/{id}")]
public sealed class UpdateClientEndpoint
{

    public static async Task<Results<Ok<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound, Conflict<StaleEntityConflictResponse>>> HandleAsync(
        UpdateClientRequest req,
        Ulid id,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IEntityFreshnessService entityFreshnessService,
        ILogger<UpdateClientEndpoint> logger,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        req.Id = id;
        var currentUserRole = (await currentUserAccessor.GetAsync(ct))?.Role.RoleName;
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        logger.LogInformation(
            "Updating client {ClientId}; fields present firstName={HasFirstName} lastName={HasLastName} patronymic={HasPatronymic} dateOfBirth={HasDateOfBirth} email={HasEmail} phone={HasPhone} telegram={HasTelegram} vk={HasVk}",
            req.Id,
            req.FirstName is not null,
            req.LastName is not null,
            req.Patronymic is not null,
            req.DateOfBirth is not null,
            req.Email is not null,
            req.Phone is not null,
            req.Telegram is not null,
            req.Vk is not null
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
                validationErrors.Add(nameof(req.SourceId), "Источник не найден");
                return TypedResults.NotFound();
            }
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "client",
            client.Id,
            req.ExpectedActivityId,
            "Клиент был изменен другим пользователем. Обновите данные или повторите сохранение поверх новой версии.",
            ct);

        if (conflict is not null && !ClientUpdateComparer.IsNoOp(client, req))
        {
            return TypedResults.Conflict(conflict);
        }

        var beforeFirstName = client.FirstName;
        var beforeLastName = client.LastName;
        var beforePatronymic = client.Patronymic;
        var beforeDateOfBirth = client.DateOfBirth;
        var beforeEmail = client.Contacts.Email;
        var beforePhone = client.Contacts.Phone;
        var beforeTelegram = client.Contacts.Telegram;
        var beforeVk = client.Contacts.Vk;
        var beforeSourceName = client.Source?.Name;
        var beforeVacations = FormatVacationPeriods(client.Vacations.Select(item => (item.StartDate, item.EndDate)));

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
        client.Contacts.Email = ClientUpdateComparer.NormalizeEmail(req.Email);
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
            Action = req.Vacations is null ? "client_updated" : "client_vacations_updated",
            EntityType = "client",
            EntityId = client.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", $"{client.LastName} {client.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeChange("Имя", beforeFirstName, client.FirstName),
                AuditDetailsFormatter.DescribeChange("Фамилия", beforeLastName, client.LastName),
                AuditDetailsFormatter.DescribeChange("Отчество", beforePatronymic, client.Patronymic),
                AuditDetailsFormatter.DescribeChange("Дата рождения", beforeDateOfBirth?.ToString("yyyy-MM-dd"), client.DateOfBirth?.ToString("yyyy-MM-dd")),
                AuditDetailsFormatter.DescribeChange("Email", beforeEmail, client.Contacts.Email),
                AuditDetailsFormatter.DescribeChange("Телефон", beforePhone, client.Contacts.Phone),
                AuditDetailsFormatter.DescribeChange("Telegram", beforeTelegram, client.Contacts.Telegram),
                AuditDetailsFormatter.DescribeChange("VK", beforeVk, client.Contacts.Vk),
                AuditDetailsFormatter.DescribeChange("Источник", beforeSourceName, client.Source?.Name),
                req.Vacations is null
                    ? null
                    : AuditDetailsFormatter.DescribeChange(
                        "Периоды отсутствия",
                        beforeVacations,
                        FormatVacationPeriods(client.Vacations.Select(item => (item.StartDate, item.EndDate))))
            )
        }, ct);

        return TypedResults.Ok(new CreateEntityResponse { Id = req.Id });
    }

    private static string? FormatVacationPeriods(IEnumerable<(DateOnly StartDate, DateOnly EndDate)> vacations)
    {
        var periods = vacations
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.EndDate)
            .Select(item => $"{item.StartDate:yyyy-MM-dd}–{item.EndDate:yyyy-MM-dd}")
            .ToArray();

        return periods.Length == 0 ? null : string.Join(", ", periods);
    }

}
