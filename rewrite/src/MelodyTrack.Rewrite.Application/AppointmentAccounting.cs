using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application;

public static class AppointmentAccounting
{
    public static Result<AppointmentAccountingChange, ErrorList> SetResult(Appointment appointment, AppointmentStatus status,
        SettlementId settlementId, Service service, IEnumerable<Payment> payments,
        IEnumerable<AppointmentSettlement> history, Actor actor, DateTimeOffset now,
        string? comment = null)
    {
        var permission = ValidateActor(actor);
        if (permission.IsFailure) return permission.Error;
        if (settlementId.Value == Guid.Empty)
            return ErrorList.Single(new IdentifierError.Empty(), "settlementId");
        var staff = (Actor.Staff)actor;
        var role = staff.Role;
        Result<Appointment, ErrorList> changed = status switch
        {
            AppointmentStatus.Completed => appointment.Complete(role, now, comment, staff.Id),
            AppointmentStatus.Burned => appointment.Burn(role, now, comment, staff.Id),
            _ => ErrorList.Single(new ModelError.InvalidValue(), "status")
        };
        if (changed.IsFailure) return changed.Error;

        var snapshot = history.ToImmutableArray();
        var state = ValidateState(appointment, snapshot);
        if (state.IsFailure) return state.Error;
        // Repeating an already recorded result must preserve its original source, even if prices or coverage changed.
        if (appointment.Status == status) return new AppointmentAccountingChange(appointment, []);

        var source = SettlementSelection.Select(changed.Value, service, payments, snapshot, now);
        if (source.IsFailure) return source.Error;
        var settlement = AppointmentSettlementRules.Create(settlementId, changed.Value, source.Value, now, snapshot);
        if (settlement.IsFailure) return settlement.Error;
        return new AppointmentAccountingChange(changed.Value, [settlement.Value]);
    }

    public static Result<AppointmentAccountingChange, ErrorList> ClearResult(Appointment appointment,
        IEnumerable<AppointmentSettlement> history, Actor actor, DateTimeOffset now, string? comment = null)
    {
        var permission = ValidateActor(actor);
        if (permission.IsFailure) return permission.Error;
        var staff = (Actor.Staff)actor;
        var changed = appointment.ClearResult(staff.Role, now, comment, staff.Id);
        if (changed.IsFailure) return changed.Error;
        var snapshot = history.ToImmutableArray();
        var state = ValidateState(appointment, snapshot);
        if (state.IsFailure) return state.Error;
        var reversed = snapshot.Where(item => item.AppointmentId == appointment.Id && !item.IsReversed)
            .Select(item => item.Reverse()).ToImmutableArray();
        return new AppointmentAccountingChange(changed.Value, reversed);
    }

    private static UnitResult<ErrorList> ValidateActor(Actor actor) => actor.IsReception()
        ? actor.Validate() : UnitResult.Failure(ErrorList.Single(new AppointmentError.Forbidden()));

    private static UnitResult<ErrorList> ValidateState(Appointment appointment, ImmutableArray<AppointmentSettlement> history)
    {
        var active = history.Where(item => item.AppointmentId == appointment.Id && !item.IsReversed).ToArray();
        var hasResult = appointment.Status is AppointmentStatus.Completed or AppointmentStatus.Burned;
        if (history.Select(item => item.Id).Distinct().Count() != history.Length || active.Length != (hasResult ? 1 : 0)
            || active.Any(item => item.ClientId != appointment.ClientId || item.ServiceId != appointment.ServiceId
                                                                        || item.StartsAt != appointment.StartsAt || item.EndsAt != appointment.EndsAt))
            return UnitResult.Failure(ErrorList.Single(new FinanceError.InconsistentSettlement()));
        return UnitResult.Success<ErrorList>();
    }

}
