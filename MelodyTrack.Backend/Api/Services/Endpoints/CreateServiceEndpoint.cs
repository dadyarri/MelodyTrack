using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Services.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MelodyTrack.Backend.Api.Services.Endpoints;

public class CreateServiceEndpoint(AppDbContext db, IAuditLogService auditLogService, IRequestReplayService requestReplayService)
    : Ep.Req<CreateServiceRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    private const string ReplayEndpoint = "services:create";

    public override void Configure()
    {
        Post("/services");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(
        CreateServiceRequest req, CancellationToken ct)
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

        var replayKey = requestReplayService.GetReplayKey(HttpContext.Request.Headers);
        if (replayKey is not null)
        {
            var existingId = await requestReplayService.TryGetResponseEntityIdAsync(ReplayEndpoint, replayKey, ct);
            if (existingId is not null)
            {
                return TypedResults.Created($"/services/{existingId}", new CreateEntityResponse
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

            var service = new Service
            {
                Id = Ulid.NewUlid(),
                Name = req.Name,
                Description = req.Description,
                IsConsultation = req.IsConsultation
            };

            var price = new ServicePrice
            {
                Id = Ulid.NewUlid(),
                Service = service,
                EffectiveDate = DateTime.UtcNow,
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

            if (replay is not null)
            {
                await requestReplayService.CompleteAsync(replay, service.Id, ct);
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
        catch (DbUpdateException ex) when (replayKey is not null && IsUniqueViolation(ex))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(ct);
            }

            var completedId = await requestReplayService.WaitForResponseEntityIdAsync(ReplayEndpoint, replayKey, ct);
            if (completedId is not null)
            {
                return TypedResults.Created($"/services/{completedId}", new CreateEntityResponse
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
