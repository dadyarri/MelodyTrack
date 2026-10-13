using MelodyTrack.Backend.Api.Vacations.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public sealed record VacationMutationResult(VacationRequestWorkflowFailure Failure, string? Detail = null, Vacation? Vacation = null);
public sealed record VacationReplacement(Ulid? Id, int? Version, DateTime StartDate, DateTime EndDate);

public sealed class VacationService(
    AppDbContext db,
    SchedulingInterruptionService interruptions,
    IAuditLogService audit,
    TimeProvider timeProvider)
{
    public static bool CanManage(User actor, Vacation vacation) => actor.Role.RoleName.IsSuperuser()
        || actor.Role.RoleName.IsClient() && actor.ClientId != null && actor.ClientId == vacation.ClientId
        || !actor.Role.RoleName.IsClient() && actor.Id == vacation.UserId;

    public static bool CanRead(User actor, Vacation vacation) => CanManage(actor, vacation) || actor.Role.RoleName.IsAnyAdmin();

    public async Task<VacationMutationResult> ReplaceAsync(VacationRequestSubjectType subjectType, Ulid subjectId,
        IReadOnlyList<VacationReplacement> replacements, bool cancelConflicts, CancellationToken ct)
    {
        await VacationRequestSubjectLock.AcquireSchedulingAsync(db, ct);
        var current = await db.Vacations.IgnoreQueryFilters().Where(item => !item.IsCancelled
            && (subjectType == VacationRequestSubjectType.Staff ? item.UserId == subjectId : item.ClientId == subjectId)).ToListAsync(ct);
        var matched = replacements.Select(item => (Input: item, Existing: item.Id is { } id
            ? current.SingleOrDefault(vacation => vacation.Id == id)
            : current.SingleOrDefault(vacation => vacation.StartDate == item.StartDate && vacation.EndDate == item.EndDate))).ToList();
        if (matched.Any(item => item.Input.Id != null && item.Existing is null)
            || matched.Where(item => item.Existing != null).GroupBy(item => item.Existing!.Id).Any(group => group.Count() > 1))
            return new(VacationRequestWorkflowFailure.Conflict, "Список отпусков изменён. Обновите данные.");
        foreach (var removed in current.Where(item => item.DeletedAtUtc == null && item.EndedAtUtc == null
            && item.EndDate > timeProvider.GetUtcNow().UtcDateTime && !matched.Any(pair => pair.Existing?.Id == item.Id)))
        {
            var result = await ApplyAsync(subjectType, subjectId, VacationRequestAction.Delete, removed.Id, removed.Version, default, default, false, ct);
            if (result.Failure != VacationRequestWorkflowFailure.None) return result;
        }
        foreach (var (input, existing) in matched)
        {
            if (existing is not null && existing.StartDate == input.StartDate && existing.EndDate == input.EndDate)
            {
                if (input.Version is not null && input.Version != existing.Version)
                    return new(VacationRequestWorkflowFailure.Conflict, "Отпуск уже изменён. Обновите данные.");
                continue;
            }
            var result = await ApplyAsync(subjectType, subjectId, existing is null ? VacationRequestAction.Create : VacationRequestAction.Update,
                existing?.Id, input.Version ?? existing?.Version, input.StartDate, input.EndDate, cancelConflicts, ct);
            if (result.Failure != VacationRequestWorkflowFailure.None) return result;
        }
        return new(VacationRequestWorkflowFailure.None);
    }

    public async Task<VacationMutationResult> ApplyAsync(
        VacationRequestSubjectType subjectType,
        Ulid subjectId,
        VacationRequestAction action,
        Ulid? vacationId,
        int? expectedVersion,
        DateTime startUtc,
        DateTime endUtc,
        bool cancelConflicts,
        CancellationToken ct)
    {
        await VacationRequestSubjectLock.AcquireSchedulingAsync(db, ct);
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var vacation = vacationId is null ? null : await db.Vacations.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.Id == vacationId, ct);
        if (action != VacationRequestAction.Create)
        {
            if (vacation is null) return new(VacationRequestWorkflowFailure.NotFound, "Отпуск не найден.");
            if ((subjectType == VacationRequestSubjectType.Staff ? vacation.UserId : vacation.ClientId) != subjectId)
                return new(VacationRequestWorkflowFailure.Forbidden);
            if (vacation.Version != expectedVersion || vacation.IsCancelled || vacation.DeletedAtUtc != null || vacation.EndedAtUtc != null)
                return new(VacationRequestWorkflowFailure.Conflict, "Отпуск уже изменён, удалён или завершён. Обновите данные.");
        }
        if (action == VacationRequestAction.Delete)
        {
            vacation!.OriginalEndDate ??= vacation.EndDate;
            vacation.DeletedAtUtc = now;
            if (vacation.StartDate >= now) vacation.IsCancelled = true;
            else if (vacation.EndDate > now) vacation.EndDate = now;
            vacation.Version++;
            await db.SaveChangesAsync(ct);
            await WriteAuditAsync(vacation, "Отпуск удалён", ct);
            return new(VacationRequestWorkflowFailure.None, Vacation: vacation);
        }
        if (startUtc.Kind != DateTimeKind.Utc || endUtc.Kind != DateTimeKind.Utc || startUtc >= endUtc)
            return new(VacationRequestWorkflowFailure.Conflict, "Укажите положительный период отпуска в UTC.");
        if (vacation is not null && vacation.StartDate == startUtc && vacation.EndDate == endUtc)
            return new(VacationRequestWorkflowFailure.None, Vacation: vacation);
        if (vacation is not null && (vacation.EndDate <= now
            || vacation.StartDate <= now && (startUtc != vacation.StartDate || endUtc <= now)))
            return new(VacationRequestWorkflowFailure.Conflict, "Историю отпуска изменять нельзя. Для текущего отпуска можно изменить будущее окончание или завершить его досрочно.");
        if (await db.Vacations.AnyAsync(item => item.Id != vacationId
            && (subjectType == VacationRequestSubjectType.Staff ? item.UserId == subjectId : item.ClientId == subjectId)
            && item.StartDate < endUtc && item.EndDate > startUtc, ct))
            return new(VacationRequestWorkflowFailure.Conflict, "Период пересекается с другим отпуском.");
        var prospective = new Vacation
        {
            Id = vacation?.Id ?? Ulid.NewUlid(),
            UserId = subjectType == VacationRequestSubjectType.Staff ? subjectId : null,
            ClientId = subjectType == VacationRequestSubjectType.Client ? subjectId : null,
            StartDate = startUtc,
            EndDate = endUtc
        };
        var conflicts = await interruptions.GetVacationConflictsAsync(prospective, ct);
        if (conflicts.Count > 0 && !cancelConflicts)
            return new(VacationRequestWorkflowFailure.Conflict, $"Пересекающихся запланированных занятий: {conflicts.Count}. Разрешите отмену или сначала измените расписание.");
        if (vacation is null)
        {
            var subjectExists = subjectType == VacationRequestSubjectType.Staff
                ? await db.Users.AnyAsync(item => item.Id == subjectId && item.ClientId == null, ct)
                : await db.Clients.AnyAsync(item => item.Id == subjectId, ct);
            if (!subjectExists) return new(VacationRequestWorkflowFailure.NotFound, "Получатель отпуска не найден.");
            vacation = prospective;
            db.Vacations.Add(vacation);
        }
        else
        {
            vacation.StartDate = startUtc;
            vacation.EndDate = endUtc;
            vacation.Version++;
        }
        await db.SaveChangesAsync(ct);
        await interruptions.SuspendForVacationAsync(vacation, conflicts, ct);
        await WriteAuditAsync(vacation, action == VacationRequestAction.Create ? "Отпуск создан" : "Отпуск изменён", ct);
        return new(VacationRequestWorkflowFailure.None, Vacation: vacation);
    }

    public async Task<VacationMutationResult> EndAsync(Ulid vacationId, int expectedVersion, User actor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await VacationRequestSubjectLock.AcquireSchedulingAsync(db, ct);
        var vacation = await db.Vacations.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.Id == vacationId, ct);
        if (vacation is null) return new(VacationRequestWorkflowFailure.NotFound, "Отпуск не найден.");
        if (!CanManage(actor, vacation)) return new(VacationRequestWorkflowFailure.Forbidden);
        if (vacation.Version != expectedVersion || vacation.IsCancelled || vacation.DeletedAtUtc != null || vacation.EndedAtUtc != null)
            return new(VacationRequestWorkflowFailure.Conflict, "Отпуск уже изменён. Обновите данные.");
        var now = timeProvider.GetUtcNow().UtcDateTime;
        if (vacation.StartDate >= now || vacation.EndDate <= now)
            return new(VacationRequestWorkflowFailure.Conflict, "Досрочно завершить можно только текущий отпуск.");
        vacation.OriginalEndDate ??= vacation.EndDate;
        vacation.EndDate = now;
        vacation.EndedAtUtc = now;
        vacation.Version++;
        await db.SaveChangesAsync(ct);
        await WriteAuditAsync(vacation, "Отпуск завершён досрочно", ct);
        await transaction.CommitAsync(ct);
        return new(VacationRequestWorkflowFailure.None, Vacation: vacation);
    }

    public async Task<VacationResponse> MapAsync(Vacation vacation, User actor, CancellationToken ct)
    {
        var timezone = vacation.UserId is { } userId
            ? vacation.User?.ScheduleTimezone ?? await db.Users.Where(item => item.Id == userId).Select(item => item.ScheduleTimezone).SingleAsync(ct)
            : "UTC";
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return new VacationResponse
        {
            Id = vacation.Id,
            SubjectType = vacation.ClientId != null ? "client" : "staff",
            SubjectId = vacation.ClientId ?? vacation.UserId!.Value,
            StartDate = vacation.StartDate,
            EndDate = vacation.EndDate,
            OriginalEndDate = vacation.OriginalEndDate,
            EndedAtUtc = vacation.EndedAtUtc,
            DeletedAtUtc = vacation.DeletedAtUtc,
            Version = vacation.Version,
            Timezone = timezone,
            State = vacation.IsCancelled || vacation.DeletedAtUtc != null ? "deleted" : vacation.EndedAtUtc != null ? "ended" : vacation.EndDate <= now ? "completed" : vacation.StartDate <= now ? "active" : "scheduled",
            RequiresApproval = !actor.Role.RoleName.IsSuperuser(),
            CanManage = CanManage(actor, vacation),
            CanEnd = CanManage(actor, vacation) && !vacation.IsCancelled && vacation.DeletedAtUtc == null && vacation.EndedAtUtc == null && vacation.StartDate < now && vacation.EndDate > now
        };
    }

    public Task WriteAuditAsync(Vacation vacation, string operation, CancellationToken ct) => audit.WriteAsync(new AuditLogWriteRequest
    {
        Event = operation switch
        {
            "Отпуск создан" => MelodyTrack.Core.Auditing.AuditCatalog.Events.VacationCreated,
            "Отпуск удалён" => MelodyTrack.Core.Auditing.AuditCatalog.Events.VacationDeleted,
            "Отпуск завершён досрочно" => MelodyTrack.Core.Auditing.AuditCatalog.Events.VacationEnded,
            _ => MelodyTrack.Core.Auditing.AuditCatalog.Events.VacationUpdated
        },
        EntityType = vacation.ClientId != null ? "client" : "user_availability",
        EntityId = (vacation.ClientId ?? vacation.UserId!.Value).ToString(),
        Details = $"{operation}; Отпуск: {vacation.Id}; Период: {vacation.StartDate:O}–{vacation.EndDate:O}; Версия: {vacation.Version}"
    }, ct);
}
