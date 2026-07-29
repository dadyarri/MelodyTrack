using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Expenses.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

public class CreateExpenseEndpoint(AppDbContext db, ICurrentUserAccessor currentUserAccessor, IAuditLogService auditLogService, IRequestReplayService requestReplayService) : Ep.Req<CreateExpenseRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult>>
{
    private const string ReplayEndpoint = "expenses:create";

    public override void Configure()
    {
        Post("/expenses");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult>> ExecuteAsync(CreateExpenseRequest req, CancellationToken ct)
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
                return TypedResults.Created($"/expenses/{decision.ResponseEntityId}", new CreateEntityResponse
                {
                    Id = decision.ResponseEntityId!.Value
                });
            }

            reservationId = decision.ReservationId;
        }

        string? categoryName = null;
        if (req.CategoryId is not null)
        {
            categoryName = await db.ExpenseCategories
                .Where(e => e.Id == req.CategoryId.Value)
                .Select(e => e.Name)
                .FirstOrDefaultAsync(ct);

            if (categoryName is null)
            {
                ThrowError("Категория расхода не найдена");
            }
        }

        var expense = new Expense
        {
            Id = Ulid.NewUlid(),
            Amount = req.Amount,
            CategoryId = req.CategoryId,
            Date = req.Date.ToUniversalTime(),
            Description = req.Description
        };

        await db.Expenses.AddAsync(expense, ct);
        await db.SaveChangesAsync(ct);

        Logger.LogInformation("Created new expense: {Description} with amount {Amount}", expense.Description, expense.Amount);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "expenses",
            Action = "expense_created",
            EntityType = "expense",
            EntityId = expense.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Описание", expense.Description),
                AuditDetailsFormatter.DescribeContext("Сумма", expense.Amount.ToString("0.##")),
                AuditDetailsFormatter.DescribeContext("Категория", categoryName),
                AuditDetailsFormatter.DescribeContext("Дата", expense.Date)
            )
        }, ct);

        if (reservationId is not null)
        {
            await requestReplayService.CompleteAsync(reservationId.Value, expense.Id, ct);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return TypedResults.Created($"/expenses/{expense.Id}", new CreateEntityResponse
        {
            Id = expense.Id
        });
    }
}
