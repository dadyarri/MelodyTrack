using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.ExpenseCategories.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MelodyTrack.Backend.Api.ExpenseCategories.Endpoints;

public class CreateExpenseCategoryEndpoint(AppDbContext db, IAuditLogService auditLogService, IRequestReplayService requestReplayService)
    : Ep.Req<CreateExpenseCategoryRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult>>
{
    private const string ReplayEndpoint = "expenseCategory:create";

    public override void Configure()
    {
        Post("/expense-categories");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult>> ExecuteAsync(CreateExpenseCategoryRequest req, CancellationToken ct)
    {
        var replayKey = requestReplayService.GetReplayKey(HttpContext.Request.Headers);
        if (replayKey is not null)
        {
            var existingId = await requestReplayService.TryGetResponseEntityIdAsync(ReplayEndpoint, replayKey, ct);
            if (existingId is not null)
            {
                return TypedResults.Created($"/expenseCategory/{existingId}", new CreateEntityResponse
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

            var expenseCategory = new ExpenseCategory
            {
                Id = Ulid.NewUlid(),
                Name = req.Name.Trim(),
            };

            await db.ExpenseCategories.AddAsync(expenseCategory, ct);
            await db.SaveChangesAsync(ct);

            Logger.LogInformation("Created new expense category: {Name}", expenseCategory.Name);
            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Category = "expense_category",
                Action = "expense_category_created",
                EntityType = "expense_category",
                EntityId = expenseCategory.Id.ToString(),
                Details = AuditDetailsFormatter.DescribeContext("Категория расхода", expenseCategory.Name)
            }, ct);

            if (replay is not null)
            {
                await requestReplayService.CompleteAsync(replay, expenseCategory.Id, ct);
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(ct);
            }

            return TypedResults.Created($"/expenses/{expenseCategory.Id}", new CreateEntityResponse
            {
                Id = expenseCategory.Id
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
