using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class VacationRules
{
    public static Result<Vacation, ErrorList> Create(VacationId id, VacationSubject subject, TimeInterval period, VacationRequestId? requestId, string? comment)
    {
        var text = Validation.Required("comment", comment);
        if (text.IsFailure) return text.Error;
        return Restore(id, subject, period, requestId, text.Value);
    }

    public static Result<Vacation, ErrorList> Restore(VacationId id, VacationSubject subject, TimeInterval period, VacationRequestId? requestId, string? comment)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), subject.Validate(),
            requestId is { } request ? Validation.Identifier(request.Value, "requestId") : UnitResult.Success<ErrorList>());
        if (validation.IsFailure) return validation.Error;
        return new Vacation(id, subject, period, requestId, comment);
    }

    public static Vacation Remove(this Vacation model) => model with { IsDeleted = true };

    public static Result<Vacation, ErrorList> ChangePeriod(this Vacation model, TimeInterval period)
    {
        if (model.IsDeleted) return ErrorList.Single(new VacationError.VacationDeleted());
        return model with { Period = period };
    }
}

public static class VacationRequestRules
{
    public static Result<VacationRequest, ErrorList> Create(VacationRequestId id, VacationSubject subject, TimeInterval period, string? comment)
    {
        if (subject is not VacationSubject.Staff) return ErrorList.Single(new ModelError.Forbidden());
        var text = Validation.Required("comment", comment);
        var validation = Validation.Collect(Validation.Identifier(id.Value), subject.Validate(), Validation.Check(text));
        if (validation.IsFailure) return validation.Error;
        return new VacationRequest(id, subject, period, text.Value);
    }

    public static Result<VacationRequest, ErrorList> CreateChange(VacationRequestId id, Vacation vacation, TimeInterval period, string? comment)
    {
        if (vacation.IsDeleted) return ErrorList.Single(new VacationError.VacationDeleted());
        var request = Create(id, vacation.Subject, period, comment);
        if (request.IsFailure) return request.Error;
        return request.Value with { Target = vacation.Id };
    }

    public static Result<VacationRequest, ErrorList> Edit(this VacationRequest model, TimeInterval period, string? comment)
    {
        if (model.Status is not VacationRequestStatus.Pending) return ErrorList.Single(new VacationError.RequestAlreadyDecided());
        var text = Validation.Required("comment", comment);
        if (text.IsFailure) return text.Error;
        return model with { Period = period, Comment = text.Value };
    }

    public static Result<VacationRequest, ErrorList> Withdraw(this VacationRequest model)
    {
        if (model.Status is not VacationRequestStatus.Pending) return ErrorList.Single(new VacationError.RequestAlreadyDecided());
        return model with { Status = new VacationRequestStatus.Withdrawn() };
    }

    public static Result<VacationRequest, ErrorList> Reject(this VacationRequest model, string? comment)
    {
        if (model.Status is not VacationRequestStatus.Pending) return ErrorList.Single(new VacationError.RequestAlreadyDecided());
        return model with { Status = new VacationRequestStatus.Rejected(), ResponseComment = Validation.OptionalText(comment) };
    }

    public static Result<VacationApproval, ErrorList> Approve(this VacationRequest model, VacationId vacationId, string? comment, IEnumerable<Appointment> appointments)
    {
        if (model.Status is not VacationRequestStatus.Pending) return ErrorList.Single(new VacationError.RequestAlreadyDecided());
        if (model.Target is { } target && target != vacationId) return ErrorList.Single(new VacationError.WrongVacation());
        if (model.Target is not null) return ErrorList.Single(new VacationError.CurrentVacationRequired());
        var vacation = VacationRules.Create(vacationId, model.Subject, model.Period, model.Id, model.Comment);
        if (vacation.IsFailure) return vacation.Error;
        var conflicts = AbsenceEffects.ApplyVacation(vacation.Value, appointments);
        if (conflicts.IsFailure) return conflicts.Error;
        return model.CompleteApproval(vacation.Value, comment);
    }

    public static Result<VacationApproval, ErrorList> ApproveChange(this VacationRequest model, Vacation vacation, string? comment, IEnumerable<Appointment> appointments)
    {
        if (model.Status is not VacationRequestStatus.Pending) return ErrorList.Single(new VacationError.RequestAlreadyDecided());
        if (model.Target != vacation.Id || model.Subject != vacation.Subject) return ErrorList.Single(new VacationError.WrongVacation());

        var changed = vacation.ChangePeriod(model.Period);
        if (changed.IsFailure) return changed.Error;
        var conflicts = AbsenceEffects.ApplyVacation(changed.Value, appointments);
        if (conflicts.IsFailure) return conflicts.Error;
        return model.CompleteApproval(changed.Value, comment);
    }

    private static VacationApproval CompleteApproval(this VacationRequest model, Vacation vacation, string? comment) => new(
        model with { Status = new VacationRequestStatus.Approved(vacation.Id), ResponseComment = Validation.OptionalText(comment) },
        vacation);
}

public static class SchoolClosureRules
{
    public static Result<SchoolClosure, ErrorList> Create(SchoolClosureId id, TimeInterval period, string? reason)
    {
        var validReason = Validation.Required("reason", reason);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validReason));
        if (validation.IsFailure) return validation.Error;
        return new SchoolClosure(id, period, validReason.Value);
    }

    public static Result<SchoolClosure, ErrorList> ChangePeriod(this SchoolClosure model, TimeInterval period)
    {
        if (model.IsDeleted) return ErrorList.Single(new SchoolClosureError.Deleted());
        return model with { Period = period };
    }

    public static Result<SchoolClosure, ErrorList> ChangeReason(this SchoolClosure model, string? reason)
    {
        if (model.IsDeleted) return ErrorList.Single(new SchoolClosureError.Deleted());
        var validReason = Validation.Required("reason", reason);
        if (validReason.IsFailure) return validReason.Error;
        return model with { Reason = validReason.Value };
    }

    public static SchoolClosure Remove(this SchoolClosure model) => model with { IsDeleted = true };
}
