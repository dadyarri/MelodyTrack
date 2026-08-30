using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.ExpenseCategories.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.ExpenseCategories.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/expense-categories")]
public sealed class CreateExpenseCategoryEndpoint
{
    private const string ReplayEndpoint = "expenseCategory:create";

    [Microsoft.AspNetCore.Authorization.Authorize(Policy = MelodyTrack.Backend.Api.Auth.AuthorizationPolicies.Administrator)]
    public static async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult>> HandleAsync(
        CreateExpenseCategoryRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IRequestReplayService requestReplayService,
        ILogger<CreateExpenseCategoryEndpoint> logger,
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
                return TypedResults.Created($"/expense-categories/{decision.ResponseEntityId}", new CreateEntityResponse
                {
                    Id = decision.ResponseEntityId!.Value
                });
            }

            reservationId = decision.ReservationId;
        }

        var expenseCategory = new ExpenseCategory
        {
            Id = Ulid.NewUlid(),
            Name = req.Name.Trim(),
        };

        await db.ExpenseCategories.AddAsync(expenseCategory, ct);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Created new expense category: {Name}", expenseCategory.Name);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.ExpenseCategoryCreated,
            EntityType = "expense_category",
            EntityId = expenseCategory.Id.ToString(),
            Details = AuditDetailsFormatter.DescribeContext("Категория расхода", expenseCategory.Name)
        }, ct);

        if (reservationId is not null)
        {
            await requestReplayService.CompleteAsync(reservationId.Value, expenseCategory.Id, ct);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return TypedResults.Created($"/expense-categories/{expenseCategory.Id}", new CreateEntityResponse
        {
            Id = expenseCategory.Id
        });
    }
}
