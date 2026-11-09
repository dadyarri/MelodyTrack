using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

public static class VacationOperations
{
    public static Result<Vacation, ErrorList> CreateClient(VacationId id, ClientId clientId, TimeInterval period,
        string? comment, IEnumerable<Appointment> appointments, Actor actor)
    {
        var validation = actor.Validate();
        if (validation.IsFailure) return validation.Error;
        if (actor is not Actor.Staff) return ErrorList.Single(new ModelError.Forbidden());
        var vacation = VacationRules.Create(id, new VacationSubject.Client(clientId), period, null, comment);
        if (vacation.IsFailure) return vacation.Error;
        var conflicts = AbsenceEffects.ApplyVacation(vacation.Value, appointments);
        return conflicts.IsFailure ? conflicts.Error : vacation.Value;
    }

    public static Result<Vacation, ErrorList> ChangePeriod(this Vacation model, DateTimeOffset now, TimeInterval period,
        IEnumerable<Appointment> appointments, Actor actor)
    {
        var validation = actor.Validate();
        if (validation.IsFailure) return validation.Error;
        if (!CanManage(model, now, actor)) return ErrorList.Single(new ModelError.Forbidden());
        if (model.Subject is VacationSubject.Staff subject && actor is Actor.Staff staff
            && (staff.Role != StaffRole.Superuser || staff.Id == subject.Id) && period.EndsAt > now)
            return ErrorList.Single(new ModelError.DecisionRequired());
        var changed = VacationRules.ChangePeriod(model, period);
        if (changed.IsFailure) return changed.Error;
        var conflicts = AbsenceEffects.ApplyVacation(changed.Value, appointments);
        return conflicts.IsFailure ? conflicts.Error : changed.Value;
    }

    public static Result<Vacation, ErrorList> Remove(this Vacation model, DateTimeOffset now, Actor actor)
    {
        var validation = actor.Validate();
        if (validation.IsFailure) return validation.Error;
        return CanManage(model, now, actor) ? VacationRules.Remove(model) : ErrorList.Single(new ModelError.Forbidden());
    }

    private static bool CanManage(Vacation model, DateTimeOffset now, Actor actor) => actor is Actor.Staff staff
        && (model.Period.StartsAt < now || model.Subject is VacationSubject.Client
            || staff.Role == StaffRole.Superuser && model.Subject is VacationSubject.Staff subject && subject.Id != staff.Id);
}

public static class VacationRequestOperations
{
    public static Result<VacationRequest, ErrorList> Create(VacationRequestId id, TimeInterval period, string? comment, Actor actor)
    {
        var validation = actor.Validate();
        if (validation.IsFailure) return validation.Error;
        if (actor is not Actor.Staff staff) return ErrorList.Single(new ModelError.Forbidden());
        return VacationRequestRules.Create(id, new VacationSubject.Staff(staff.Id), period, comment);
    }

    public static Result<VacationApproval, ErrorList> Approve(this VacationRequest model, VacationId id, string? comment,
        IEnumerable<Appointment> appointments, Actor actor)
    {
        var validation = actor.Validate();
        if (validation.IsFailure) return validation.Error;
        if (!CanApprove(model, actor)) return ErrorList.Single(new ModelError.Forbidden());
        return VacationRequestRules.Approve(model, id, comment, appointments);
    }

    public static Result<VacationApproval, ErrorList> ApproveChange(this VacationRequest model, Vacation vacation, string? comment,
        IEnumerable<Appointment> appointments, Actor actor)
    {
        var validation = actor.Validate();
        if (validation.IsFailure) return validation.Error;
        if (!CanApprove(model, actor)) return ErrorList.Single(new ModelError.Forbidden());
        return VacationRequestRules.ApproveChange(model, vacation, comment, appointments);
    }

    private static bool CanApprove(VacationRequest model, Actor actor) => actor is Actor.Staff { Role: StaffRole.Superuser } staff
        && model.Subject is VacationSubject.Staff subject && subject.Id != staff.Id;
}

public static class SchoolClosureOperations
{
    public static Result<SchoolClosure, ErrorList> Create(SchoolClosureId id, TimeInterval period, string? reason,
        IEnumerable<Appointment> appointments, Actor actor)
    {
        var validation = actor.Validate();
        if (validation.IsFailure) return validation.Error;
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        var closure = SchoolClosureRules.Create(id, period, reason);
        if (closure.IsFailure) return closure.Error;
        var conflicts = AbsenceEffects.ApplyClosure(closure.Value, appointments);
        return conflicts.IsFailure ? conflicts.Error : closure.Value;
    }

    public static Result<SchoolClosure, ErrorList> ChangePeriod(this SchoolClosure model, TimeInterval period,
        IEnumerable<Appointment> appointments, Actor actor)
    {
        var validation = actor.Validate();
        if (validation.IsFailure) return validation.Error;
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        var changed = SchoolClosureRules.ChangePeriod(model, period);
        if (changed.IsFailure) return changed.Error;
        var conflicts = AbsenceEffects.ApplyClosure(changed.Value, appointments);
        return conflicts.IsFailure ? conflicts.Error : changed.Value;
    }
}
