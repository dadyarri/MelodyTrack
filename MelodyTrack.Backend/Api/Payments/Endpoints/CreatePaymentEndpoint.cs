using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Payments.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Payments.Endpoints;

public class CreatePaymentEndpoint(
    AppDbContext db, ICurrentUserAccessor currentUserAccessor,
    IAuditLogService auditLogService,
    IRequestReplayService requestReplayService)
    : Ep.Req<CreatePaymentRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>>
{
    private const string ReplayEndpoint = "payments:create";

    public override void Configure()
    {
        Post("/payments");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> ExecuteAsync(CreatePaymentRequest req, CancellationToken ct)
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

        var replayKey = requestReplayService.GetReplayKey(HttpContext.Request.Headers);
        await using var transaction = replayKey is null ? null : await db.Database.BeginTransactionAsync(ct);
        Ulid? reservationId = null;
        if (replayKey is not null)
        {
            var decision = await requestReplayService.AcquireAsync(ReplayEndpoint, replayKey, req, ct);
            if (decision.Status == RequestReplayStatus.Completed)
            {
                return TypedResults.Created($"/payments/{decision.ResponseEntityId}", new CreateEntityResponse
                {
                    Id = decision.ResponseEntityId!.Value
                });
            }

            reservationId = decision.ReservationId;
        }

        Service? service = null;
        if (req.ServiceId.HasValue)
        {
            service = await db.Services
                .Where(e => e.Id == req.ServiceId.Value)
                .FirstOrDefaultAsync(ct);

            if (service is null)
            {
                AddError(r => r.ServiceId, "Сервис не найден");
                return TypedResults.NotFound(new ApiProblemDetails(ValidationFailures, HttpContext, StatusCodes.Status404NotFound));
            }
        }

        var client = await db.Clients.Where(e => e.Id == req.ClientId)
            .FirstOrDefaultAsync(ct);

        if (client is null)
        {
            AddError(r => r.ClientId, "Клиент не найден");
            return TypedResults.NotFound(new ApiProblemDetails(ValidationFailures, HttpContext, StatusCodes.Status404NotFound));
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
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", $"{client.LastName} {client.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeContext("Услуга", service?.Name),
                AuditDetailsFormatter.DescribeContext("Сумма", payment.Amount.ToString("0.##")),
                AuditDetailsFormatter.DescribeContext("Дата", payment.Date),
                AuditDetailsFormatter.DescribeContext("Описание", payment.Description)
            )
        }, ct);

        if (reservationId is not null)
        {
            await requestReplayService.CompleteAsync(reservationId.Value, payment.Id, ct);
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
}
