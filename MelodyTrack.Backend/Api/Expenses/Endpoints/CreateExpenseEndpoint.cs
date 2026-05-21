using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Expenses.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

public class CreateExpenseEndpoint(AppDbContext db, IAuditLogService auditLogService, IRequestReplayService requestReplayService) : Ep.Req<CreateExpenseRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult>>
{
    private const string ReplayEndpoint = "expenses:create";

    public override void Configure()
    {
        Post("/expenses");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult>> ExecuteAsync(CreateExpenseRequest req, CancellationToken ct)
    {
        var replayKey = requestReplayService.GetReplayKey(HttpContext.Request.Headers);
        if (replayKey is not null)
        {
            var existingId = await requestReplayService.TryGetResponseEntityIdAsync(ReplayEndpoint, replayKey, ct);
            if (existingId is not null)
            {
                return TypedResults.Created($"/expenses/{existingId}", new CreateEntityResponse
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

            if (req.CategoryId is not null)
            {
                var categoryExists = await db.ExpenseCategories.AnyAsync(e => e.Id == req.CategoryId.Value, ct);
                if (!categoryExists)
                {
                    ThrowError("Категория расхода не найдена");
                }
            }

            var expense = new Expense
            {
                Id = Ulid.NewUlid(),
                Amount = req.Amount,
                CategoryId = req.CategoryId,
                Date = DateTime.UtcNow,
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
                Details = $"{expense.Description}, сумма {expense.Amount}"
            }, ct);

            if (replay is not null)
            {
                await requestReplayService.CompleteAsync(replay, expense.Id, ct);
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
        catch (DbUpdateException ex) when (replayKey is not null && IsUniqueViolation(ex))
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(ct);
            }

            var completedId = await requestReplayService.WaitForResponseEntityIdAsync(ReplayEndpoint, replayKey, ct);
            if (completedId is not null)
            {
                return TypedResults.Created($"/expenses/{completedId}", new CreateEntityResponse
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
