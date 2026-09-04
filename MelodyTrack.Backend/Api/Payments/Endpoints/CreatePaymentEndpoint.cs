using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
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

[ApiEndpoint(ApiMethod.Post, "/payments")]
public sealed class CreatePaymentEndpoint
{
    private const string ReplayEndpoint = "payments:create";

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> HandleAsync(
        CreatePaymentRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IRequestReplayService requestReplayService,
        ILogger<CreatePaymentEndpoint> logger,
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
                validationErrors.Add(nameof(req.ServiceId), "Сервис не найден");
                return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
            }
        }

        var client = await db.Clients.Where(e => e.Id == req.ClientId)
            .FirstOrDefaultAsync(ct);

        if (client is null)
        {
            validationErrors.Add(nameof(req.ClientId), "Клиент не найден");
            return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
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

        logger.LogInformation("Created new payment: {Description} with amount {Amount}", payment.Description, payment.Amount);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.PaymentCreated,
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
