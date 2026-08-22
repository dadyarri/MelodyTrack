using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Services.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/services")]
public sealed class CreateServiceEndpoint
{
    private const string ReplayEndpoint = "services:create";

    public static async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        CreateServiceRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IRequestReplayService requestReplayService,
        TimeProvider timeProvider,
        HttpContext httpContext,
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
                return TypedResults.Created($"/services/{decision.ResponseEntityId}", new CreateEntityResponse
                {
                    Id = decision.ResponseEntityId!.Value
                });
            }

            reservationId = decision.ReservationId;
        }

        var service = new Service
        {
            Id = Ulid.NewUlid(),
            Name = req.Name,
            PublicName = req.PublicName,
            Description = req.Description,
            IsConsultation = req.IsConsultation
        };

        var price = new ServicePrice
        {
            Id = Ulid.NewUlid(),
            Service = service,
            EffectiveDate = timeProvider.GetUtcNow().UtcDateTime,
            Price = req.Price
        };

        await db.Services.AddAsync(service, ct);
        await db.ServicePriceHistory.AddAsync(price, ct);
        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "services",
            Action = "service_created",
            EntityType = "service",
            EntityId = service.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Услуга", service.Name),
                AuditDetailsFormatter.DescribeContext("Описание", service.Description),
                AuditDetailsFormatter.DescribeContext("Консультация", service.IsConsultation ? "Да" : "Нет"),
                AuditDetailsFormatter.DescribeContext("Цена", req.Price.ToString("0.##"))
            )
        }, ct);

        if (reservationId is not null)
        {
            await requestReplayService.CompleteAsync(reservationId.Value, service.Id, ct);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return TypedResults.Created($"/services/{service.Id}", new CreateEntityResponse
        {
            Id = service.Id
        });
    }
}
