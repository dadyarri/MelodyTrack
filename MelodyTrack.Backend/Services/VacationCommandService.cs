using MelodyTrack.Backend.Api.VacationRequests.Requests;
using MelodyTrack.Backend.Api.Vacations.Requests;
using MelodyTrack.Backend.Api.Vacations.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public sealed record VacationCommandResult(VacationRequestWorkflowFailure Failure, string? Detail = null, VacationOperationResponse? Response = null);

public sealed class VacationCommandService(
    AppDbContext db,
    VacationService vacations,
    IVacationRequestWorkflowService workflow,
    IVacationRequestQueryService requests)
{
    public async Task<VacationCommandResult> CreateAsync(User actor, CreateVacationInput input, CancellationToken ct)
    {
        var client = actor.Role.RoleName.IsClient();
        var subjectType = client ? VacationRequestSubjectType.Client : VacationRequestSubjectType.Staff;
        var subjectId = client ? actor.ClientId : actor.Id;
        if (actor.Role.RoleName.IsSuperuser())
        {
            if (input.SubjectType is not (null or "staff" or "client")) return new(VacationRequestWorkflowFailure.Conflict, "Некорректный тип получателя отпуска.");
            subjectType = input.SubjectType == "client" ? VacationRequestSubjectType.Client : VacationRequestSubjectType.Staff;
            subjectId = input.SubjectId ?? actor.Id;
        }
        else if (input.SubjectId is { } requestedId && requestedId != subjectId
            || input.SubjectType is not null && input.SubjectType != (client ? "client" : "staff"))
            return new(VacationRequestWorkflowFailure.Forbidden);
        if (subjectId is null) return new(VacationRequestWorkflowFailure.Forbidden);
        if (!actor.Role.RoleName.IsSuperuser())
        {
            var request = new CreateVacationRequest { StartDate = input.StartDate, EndDate = input.EndDate, Message = input.Message };
            return await MapRequestAsync(client ? await workflow.CreateClientRequestAsync(actor, request, ct) : await workflow.CreateStaffRequestAsync(actor, request, ct), actor, ct);
        }
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var result = await vacations.ApplyAsync(subjectType, subjectId.Value, VacationRequestAction.Create, null, null,
            input.StartDate, input.EndDate, input.CancelConflictingAppointments, ct);
        if (result.Failure != VacationRequestWorkflowFailure.None) return new(result.Failure, result.Detail);
        await transaction.CommitAsync(ct);
        return new(VacationRequestWorkflowFailure.None, Response: new VacationOperationResponse
        {
            RequiresApproval = false,
            Vacation = await vacations.MapAsync(result.Vacation!, actor, ct)
        });
    }

    public async Task<VacationCommandResult> ChangeAsync(User actor, Ulid id, int expectedVersion, VacationRequestAction action,
        DateTime startUtc, DateTime endUtc, string? message, bool cancelConflicts, CancellationToken ct)
    {
        if (!actor.Role.RoleName.IsSuperuser())
            return await MapRequestAsync(await workflow.CreateChangeRequestAsync(actor, id, expectedVersion, action, startUtc, endUtc, message, ct), actor, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await VacationRequestSubjectLock.AcquireSchedulingAsync(db, ct);
        var vacation = await db.Vacations.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.Id == id, ct);
        if (vacation is null) return new(VacationRequestWorkflowFailure.NotFound, "Отпуск не найден.");
        var type = vacation.ClientId != null ? VacationRequestSubjectType.Client : VacationRequestSubjectType.Staff;
        var result = await vacations.ApplyAsync(type, vacation.ClientId ?? vacation.UserId!.Value, action, id, expectedVersion,
            startUtc, endUtc, cancelConflicts, ct);
        if (result.Failure != VacationRequestWorkflowFailure.None) return new(result.Failure, result.Detail);
        await transaction.CommitAsync(ct);
        return new(VacationRequestWorkflowFailure.None, Response: new VacationOperationResponse
        {
            RequiresApproval = false,
            Vacation = await vacations.MapAsync(result.Vacation!, actor, ct)
        });
    }

    private async Task<VacationCommandResult> MapRequestAsync(VacationRequestWorkflowResult result, User actor, CancellationToken ct)
    {
        if (result.Failure != VacationRequestWorkflowFailure.None) return new(result.Failure, result.Detail);
        return new(VacationRequestWorkflowFailure.None, Response: new VacationOperationResponse
        {
            RequiresApproval = true,
            Request = await requests.GetAccessibleAsync(result.Request!.Id, actor, ct)
        });
    }
}
