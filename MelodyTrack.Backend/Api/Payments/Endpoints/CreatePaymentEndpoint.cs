using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Payments.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MelodyTrack.Backend.Api.Payments.Endpoints;

public class CreatePaymentEndpoint(AppDbContext db, IAuditLogService auditLogService, IRequestReplayService requestReplayService) : Ep.Req<CreatePaymentRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, NotFound<ProblemDetails>>>
{
    private const string ReplayEndpoint = "payments:create";

    public override void Configure()
    {
        Post("/payments");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, NotFound<ProblemDetails>>> ExecuteAsync(CreatePaymentRequest req, CancellationToken ct)
    {
        var replayKey = requestReplayService.GetReplayKey(HttpContext.Request.Headers);
        if (replayKey is not null)
        {
            var existingId = await requestReplayService.TryGetResponseEntityIdAsync(ReplayEndpoint, replayKey, ct);
            if (existingId is not null)
            {
                return TypedResults.Created($"/payments/{existingId}", new CreateEntityResponse
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

            var service = await db.Services.Where(e => e.Id == req.ServiceId)
                .FirstOrDefaultAsync(ct);

            if (service is null)
            {
                AddError(r => r.ServiceId, "Сервис не найден");
                return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
            }

            var client = await db.Clients.Where(e => e.Id == req.ClientId)
                .FirstOrDefaultAsync(ct);

            if (client is null)
            {
                AddError(r => r.ClientId, "Клиент не найден");
                return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
            }

            var payment = new Payment
            {
                Id = Ulid.NewUlid(),
                Amount = req.Amount,
                Client = client,
                Date = req.Date,
                Description = req.Description ?? string.Empty,
                Service = service
            };

            await db.Payments.AddAsync(payment, ct);
            await db.SaveChangesAsync(ct);

            Logger.LogInformation("Created new payment: {Description} with amount {Amount}", payment.Description, payment.Amount);
            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Category = "payments",
                Action = "payment_created",
                EntityType = "payment",
                EntityId = payment.Id.ToString(),
                Details = $"{client.LastName} {client.FirstName}, сумма {payment.Amount}"
            }, ct);

            if (replay is not null)
            {
                await requestReplayService.CompleteAsync(replay, payment.Id, ct);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }

            return TypedResults.Created($"/payments/{payment.Id}", new CreateEntityResponse
            {
                Id = payment.Id
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
                return TypedResults.Created($"/payments/{completedId}", new CreateEntityResponse
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
