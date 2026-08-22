using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Tasks.Endpoints;

[ApiEndpoint(ApiMethod.Post, "/tasks")]
public sealed class CreateCustomTaskEndpoint
{

    public static async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>>> HandleAsync(
        CreateCustomTaskRequest req,
        AppDbContext db,
        IAuditLogService auditLogService,
        ICurrentUserAccessor currentUserAccessor,
        TimeProvider timeProvider,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (!TaskAccess.CanAccessTasks(currentUser))
        {
            return TypedResults.Forbid();
        }

        Client? client = null;
        if (req.ClientId.HasValue)
        {
            client = await db.Clients
                .Include(item => item.Contacts)
                .FirstOrDefaultAsync(item => item.Id == req.ClientId.Value, ct);

            if (client is null)
            {
                validationErrors.Add(nameof(req.ClientId), "Клиент не найден");
                return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
            }
        }

        var dueAtUtc = req.DueAtUtc.Kind switch
        {
            DateTimeKind.Utc => req.DueAtUtc,
            DateTimeKind.Local => req.DueAtUtc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(req.DueAtUtc, DateTimeKind.Utc)
        };

        var task = new CustomTask
        {
            Id = Ulid.NewUlid(),
            ClientId = client?.Id,
            Client = client,
            RecipientName = client is null
                ? req.RecipientName!.Trim()
                : string.Join(' ', new[] { client.LastName, client.FirstName, client.Patronymic }.Where(value => !string.IsNullOrWhiteSpace(value))),
            Phone = client?.Contacts.Phone ?? req.Phone?.Trim(),
            Telegram = client?.Contacts.Telegram ?? req.Telegram?.Trim(),
            Vk = client?.Contacts.Vk ?? req.Vk?.Trim(),
            Title = req.Title.Trim(),
            MessageText = req.MessageText.Trim(),
            DueAtUtc = dueAtUtc,
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            CreatedByUserId = currentUser.Id
        };

        await db.CustomTasks.AddAsync(task, ct);
        await db.SaveChangesAsync(ct);

        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "recurring_tasks",
            Action = "custom_task_created",
            EntityType = "custom_task",
            EntityId = task.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Тип", RecurringTaskType.CustomTask.ToApiKey()),
                AuditDetailsFormatter.DescribeContext("Задача", task.Title),
                AuditDetailsFormatter.DescribeContext("Получатель", task.RecipientName),
                AuditDetailsFormatter.DescribeContext("Дата", task.DueAtUtc),
                AuditDetailsFormatter.DescribeContext("Текст", task.MessageText))
        }, ct);

        return TypedResults.Created($"/tasks/{task.Id}", new CreateEntityResponse
        {
            Id = task.Id
        });
    }
}
