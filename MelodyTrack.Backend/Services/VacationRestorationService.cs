using MelodyTrack.Backend.Api.Vacations.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Core.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public sealed class VacationRestorationService(
    AppDbContext db,
    IRecurringAppointmentService recurrence,
    ISchedulingAvailabilityService availability,
    SchedulingInterruptionService interruptions,
    TimeProvider timeProvider)
{
    public async Task<IReadOnlyList<VacationRestorationCandidateResponse>> GetCandidatesAsync(Ulid vacationId, CancellationToken ct)
    {
        var rows = await LoadAsync(vacationId, ct);
        var result = new List<VacationRestorationCandidateResponse>();
        foreach (var row in rows)
        {
            var appointment = Resolve(row);
            var blockers = appointment is null
                ? [new SchedulingBlocker("unavailable_occurrence", "Занятие или серия удалены либо уже изменены.")]
                : await availability.EvaluateAsync(appointment.Client.Id, appointment.Provider?.Id, appointment.StartDate, appointment.EndDate,
                    appointment.Id == row.AppointmentId ? [appointment.Id] : null, ct);
            result.Add(new VacationRestorationCandidateResponse
            {
                Id = row.Id,
                StartDate = appointment?.StartDate ?? row.StartDate,
                EndDate = appointment?.EndDate ?? row.EndDate,
                ServiceName = appointment?.Service.Name ?? "Занятие недоступно",
                Available = blockers.Count == 0,
                Blockers = blockers.Select(item => item.Message).Distinct().ToList()
            });
        }
        return result;
    }

    public async Task<VacationMutationResult> RestoreAsync(Ulid vacationId, int expectedVersion, IReadOnlyCollection<Ulid> selectedIds, User actor, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await VacationRequestSubjectLock.AcquireSchedulingAsync(db, ct);
        var vacation = await db.Vacations.IgnoreQueryFilters().SingleOrDefaultAsync(item => item.Id == vacationId, ct);
        if (vacation is null) return new(VacationRequestWorkflowFailure.NotFound, "Отпуск не найден.");
        if (!VacationService.CanManage(actor, vacation)) return new(VacationRequestWorkflowFailure.Forbidden);
        if (vacation.Version != expectedVersion) return new(VacationRequestWorkflowFailure.Conflict, "Отпуск изменён. Обновите список занятий.");
        var rows = (await LoadAsync(vacationId, ct)).Where(item => selectedIds.Contains(item.Id)).ToList();
        if (selectedIds.Count == 0 || selectedIds.Distinct().Count() != rows.Count)
            return new(VacationRequestWorkflowFailure.Conflict, "Выбранные занятия уже изменены или недоступны для восстановления.");
        var prepared = new List<(SchedulingInterruption Interruption, Appointment Appointment)>();
        foreach (var row in rows)
        {
            var appointment = Resolve(row);
            if (appointment is null) return new(VacationRequestWorkflowFailure.Conflict, "Занятие или серия уже изменены.");
            var blockers = await availability.EvaluateAsync(appointment.Client.Id, appointment.Provider?.Id, appointment.StartDate, appointment.EndDate,
                appointment.Id == row.AppointmentId ? [appointment.Id] : null, ct);
            if (blockers.Count > 0) return new(VacationRequestWorkflowFailure.Conflict, string.Join(" ", blockers.Select(item => item.Message).Distinct()));
            if (prepared.Any(item => SchedulingAvailability.Overlaps(appointment.StartDate, appointment.EndDate, item.Appointment.StartDate, item.Appointment.EndDate)
                && (appointment.Client.Id == item.Appointment.Client.Id || appointment.Provider != null && appointment.Provider.Id == item.Appointment.Provider?.Id)))
                return new(VacationRequestWorkflowFailure.Conflict, "Выбранные занятия пересекаются между собой.");
            prepared.Add((row, appointment));
        }
        var now = timeProvider.GetUtcNow().UtcDateTime;
        foreach (var (row, appointment) in prepared)
        {
            if (row.AppointmentId is null)
            {
                var stored = await db.Appointments.SingleOrDefaultAsync(item => item.RecurringRuleId == appointment.RecurringRuleId
                    && item.RecurrenceOrdinal == appointment.RecurrenceOrdinal, ct);
                if (stored is not null) return new(VacationRequestWorkflowFailure.Conflict, "Вхождение серии уже существует. Обновите данные.");
                db.Appointments.Add(appointment);
                row.Appointment = appointment;
                row.AppointmentId = appointment.Id;
            }
            appointment.Status = AppointmentStatus.Planned;
            row.RestoredAtUtc = now;
            await interruptions.WriteAppointmentAuditAsync(appointment, AppointmentStatus.Cancelled, "Восстановлено по явному выбору после изменения отпуска", ct);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new(VacationRequestWorkflowFailure.None, Vacation: vacation);
    }

    private Task<List<SchedulingInterruption>> LoadAsync(Ulid vacationId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        return db.SchedulingInterruptions
            .Include(item => item.Appointment).ThenInclude(item => item!.Client)
            .Include(item => item.Appointment).ThenInclude(item => item!.Service)
            .Include(item => item.Appointment).ThenInclude(item => item!.Provider)
            .Include(item => item.RecurringRule).ThenInclude(item => item!.Client)
            .Include(item => item.RecurringRule).ThenInclude(item => item!.Service)
            .Include(item => item.RecurringRule).ThenInclude(item => item!.Provider)
            .Include(item => item.RecurringRule).ThenInclude(item => item!.RecurrenceType)
            .Where(item => item.VacationId == vacationId && item.RestoredAtUtc == null && item.SupersededAtUtc == null && item.StartDate > now)
            .OrderBy(item => item.StartDate).ToListAsync(ct);
    }

    private Appointment? Resolve(SchedulingInterruption row)
    {
        if (row.Appointment is { } appointment)
            return appointment.IsDeleted || appointment.Status != AppointmentStatus.Cancelled ? null : appointment;
        if (row.RecurringRule is null || row.RecurrenceOrdinal is null) return null;
        return recurrence.GetOccurrence(row.RecurringRule, row.RecurrenceOrdinal.Value);
    }
}
