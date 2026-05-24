using FastEndpoints;
using MelodyTrack.Backend.Api.Clients.Requests;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MelodyTrack.Backend.Api.Clients.Endpoints;

public class
    CreateClientEndpoint(AppDbContext db, IAuditLogService auditLogService, IRequestReplayService requestReplayService)
    : Ep.Req<CreateClientRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, NotFound<ProblemDetails>>>
{
    private const string ReplayEndpoint = "clients:create";

    public override void Configure()
    {
        Post("/clients");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, NotFound<ProblemDetails>>> ExecuteAsync(
        CreateClientRequest req, CancellationToken ct)
    {
        var replayKey = requestReplayService.GetReplayKey(HttpContext.Request.Headers);
        if (replayKey is not null)
        {
            var existingId = await requestReplayService.TryGetResponseEntityIdAsync(ReplayEndpoint, replayKey, ct);
            if (existingId is not null)
            {
                return TypedResults.Created($"/clients/{existingId}", new CreateEntityResponse
                {
                    Id = existingId.Value
                });
            }
        }

        Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction? transaction = null;
        RequestReplay? replay = null;

        try
        {
            if (replayKey is not null)
            {
                transaction = await db.Database.BeginTransactionAsync(ct);
                replay = await requestReplayService.ReserveAsync(ReplayEndpoint, replayKey, ct);
            }

            ClientSource? source = null;
            if (req.SourceId is not null)
            {
                source = await db.ClientSources.FirstOrDefaultAsync(e => e.Id == req.SourceId.Value, ct);
                if (source is null)
                {
                    AddError(e => e.SourceId, "Источник не найден");
                    return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
                }
            }

            var client = new Client
            {
                Id = Ulid.NewUlid(),
                FirstName = req.FirstName,
                LastName = req.LastName,
                Patronymic = req.Patronymic,
                Source = source,
                CreatedAtUtc = DateTime.UtcNow,
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
                Details = AuditDetailsFormatter.JoinChanges(
                    AuditDetailsFormatter.DescribeContext("Клиент", $"{client.LastName} {client.FirstName}".Trim()),
                    AuditDetailsFormatter.DescribeContext("Отчество", client.Patronymic),
                    AuditDetailsFormatter.DescribeContext("Телефон", client.Contacts.Phone),
                    AuditDetailsFormatter.DescribeContext("Telegram", client.Contacts.Telegram),
                    AuditDetailsFormatter.DescribeContext("VK", client.Contacts.Vk),
                    AuditDetailsFormatter.DescribeContext("Источник", source?.Name)
                )
            }, ct);

            if (replay is not null)
            {
                await requestReplayService.CompleteAsync(replay, client.Id, ct);
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
        catch (DbUpdateException ex) when (replayKey is not null && IsUniqueViolation(ex))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(ct);
            }

            var completedId = await requestReplayService.WaitForResponseEntityIdAsync(ReplayEndpoint, replayKey, ct);
            if (completedId is not null)
            {
                return TypedResults.Created($"/clients/{completedId}", new CreateEntityResponse
                {
                    Id = completedId.Value
                });
            }

            throw;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException exception)
    {
        return exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
    }
}
