using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/clients")]
public sealed class CreateClientEndpoint
{
    private const string ReplayEndpoint = "clients:create";

    public static async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, Conflict<ApiProblemDetails>>> HandleAsync(
        CreateClientRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IRequestReplayService requestReplayService,
        TimeProvider timeProvider,
        ILogger<CreateClientEndpoint> logger,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        var currentUserRole = (await currentUserAccessor.GetAsync(ct))?.Role.RoleName;
        if (currentUserRole is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!currentUserRole.Value.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        var replayKey = requestReplayService.GetReplayKey(httpContext.Request.Headers);
        await using var transaction = replayKey is null ? null : await db.Database.BeginTransactionAsync(ct);
        Ulid? reservationId = null;
        if (replayKey is not null)
        {
            var decision = await requestReplayService.AcquireAsync(ReplayEndpoint, replayKey, req, ct);
            if (decision.Status == RequestReplayStatus.Completed)
            {
                return TypedResults.Created($"/clients/{decision.ResponseEntityId}", new CreateEntityResponse
                {
                    Id = decision.ResponseEntityId!.Value
                });
            }

            reservationId = decision.ReservationId;
        }

        ClientSource? source = null;
        if (req.SourceId is not null)
        {
            source = await db.ClientSources.FirstOrDefaultAsync(e => e.Id == req.SourceId.Value, ct);
            if (source is null)
            {
                validationErrors.Add(nameof(req.SourceId), "Источник не найден");
                return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
            }
        }

        var duplicateContactField = await FindDuplicateContactFieldAsync(db, req, ct);
        if (duplicateContactField is not null)
        {
            validationErrors.Add(duplicateContactField, "Этот контакт уже указан у другого клиента.");
            return TypedResults.Conflict(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status409Conflict));
        }

        var client = new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = req.FirstName,
            LastName = req.LastName,
            Patronymic = req.Patronymic,
            DateOfBirth = req.DateOfBirth,
            Source = source,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Contacts = new ClientContacts
            {
                Id = Ulid.NewUlid(),
                Email = string.IsNullOrWhiteSpace(req.Email) ? null : UserUtils.NormalizeEmail(req.Email),
                Telegram = req.Telegram,
                Phone = req.Phone,
                Vk = req.Vk
            }
        };

        await db.Clients.AddAsync(client, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "Created new client: {FirstName} {LastName} (ID: {ClientId}); contact presence email={HasEmail} phone={HasPhone} telegram={HasTelegram} vk={HasVk}",
            client.FirstName,
            client.LastName,
            client.Id,
            client.Contacts.Email is not null,
            client.Contacts.Phone is not null,
            client.Contacts.Telegram is not null,
            client.Contacts.Vk is not null
        );
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "clients",
            Action = "client_created",
            EntityType = "client",
            EntityId = client.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", $"{client.LastName} {client.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeContext("Отчество", client.Patronymic),
                AuditDetailsFormatter.DescribeContext("Дата рождения", client.DateOfBirth?.ToString("yyyy-MM-dd")),
                AuditDetailsFormatter.DescribeContext("Email", client.Contacts.Email),
                AuditDetailsFormatter.DescribeContext("Телефон", client.Contacts.Phone),
                AuditDetailsFormatter.DescribeContext("Telegram", client.Contacts.Telegram),
                AuditDetailsFormatter.DescribeContext("VK", client.Contacts.Vk),
                AuditDetailsFormatter.DescribeContext("Источник", source?.Name)
            )
        }, ct);

        if (reservationId is not null)
        {
            await requestReplayService.CompleteAsync(reservationId.Value, client.Id, ct);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return TypedResults.Created($"/clients/{client.Id}", new CreateEntityResponse
        {
            Id = client.Id
        });
    }

    private static async Task<string?> FindDuplicateContactFieldAsync(AppDbContext db, CreateClientRequest request, CancellationToken ct)
    {
        if (new[] { request.Email, request.Phone, request.Telegram, request.Vk }.All(string.IsNullOrWhiteSpace))
        {
            return null;
        }

        var contacts = await db.Clients
            .AsNoTracking()
            .Select(client => client.Contacts)
            .ToListAsync(ct);
        var email = NormalizeContact(request.Email);
        var phone = NormalizePhone(request.Phone);
        var telegram = NormalizeContact(request.Telegram);
        var vk = NormalizeContact(request.Vk);

        if (email is not null && contacts.Any(contact => NormalizeContact(contact.Email) == email))
        {
            return nameof(request.Email);
        }

        if (phone is not null && contacts.Any(contact => NormalizePhone(contact.Phone) == phone))
        {
            return nameof(request.Phone);
        }

        if (telegram is not null && contacts.Any(contact => NormalizeContact(contact.Telegram) == telegram))
        {
            return nameof(request.Telegram);
        }

        return vk is not null && contacts.Any(contact => NormalizeContact(contact.Vk) == vk)
            ? nameof(request.Vk)
            : null;
    }

    private static string? NormalizeContact(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var digits = new string(value.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits;
    }
}
