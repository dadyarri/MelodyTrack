using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Expenses.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using MelodyTrack.Backend.ErrorHandling;

namespace MelodyTrack.Backend.Api.Expenses.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/expenses")]
public sealed class CreateExpenseEndpoint
{
    private const string ReplayEndpoint = "expenses:create";

    public static async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> HandleAsync(
        CreateExpenseRequest req,
        AppDbContext db,
        ICurrentUserAccessor currentUserAccessor,
        IAuditLogService auditLogService,
        IRequestReplayService requestReplayService,
        ILogger<CreateExpenseEndpoint> logger,
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
                validationErrors.Add(nameof(req.CategoryId), "Категория расхода не найдена");
                return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
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

        logger.LogInformation("Created new expense: {Description} with amount {Amount}", expense.Description, expense.Amount);
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
