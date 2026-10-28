using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public closed record VacationSubject
{
    public sealed record Client(ClientId Id) : VacationSubject;
    public sealed record Staff(StaffId Id) : VacationSubject;

    internal UnitResult<ErrorList> Validate() => this switch
    {
        Client client => Validation.Identifier(client.Id.Value, "clientId"),
        Staff staff => Validation.Identifier(staff.Id.Value, "staffId")
    };
}

public sealed record Vacation
{
    private Vacation(VacationId id, VacationSubject subject, TimeInterval period, VacationRequestId? requestId)
    {
        Id = id; Subject = subject; Period = period; RequestId = requestId;
    }
    public VacationId Id { get; }
    public VacationSubject Subject { get; }
    public TimeInterval Period { get; private init; }
    public VacationRequestId? RequestId { get; }
    public bool IsDeleted { get; private init; }

    public static Result<Vacation, ErrorList> Create(VacationId id, VacationSubject subject, TimeInterval period, VacationRequestId? requestId)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), subject.Validate(),
            requestId is { } request ? Validation.Identifier(request.Value, "requestId") : UnitResult.Success<ErrorList>());
        if (validation.IsFailure) return validation.Error;
        return new Vacation(id, subject, period, requestId);
    }

    public Vacation Remove() => this with { IsDeleted = true };
    public Result<Vacation, ErrorList> ChangePeriod(TimeInterval period)
    {
        if (IsDeleted) return ErrorList.Of(new VacationError.VacationDeleted());
        return this with { Period = period };
    }
}

public closed record VacationRequestStatus
{
    public sealed record Pending : VacationRequestStatus;
    public sealed record Approved(VacationId VacationId) : VacationRequestStatus;
    public sealed record Rejected : VacationRequestStatus;
    public sealed record Withdrawn : VacationRequestStatus;
}

public sealed record VacationApproval(VacationRequest Request, Vacation Vacation);

public sealed record VacationRequest
{
    private VacationRequest(VacationRequestId id, VacationSubject subject, TimeInterval period, string? comment)
    {
        Id = id; Subject = subject; Period = period; Comment = comment;
    }
    public VacationRequestId Id { get; }
    public VacationSubject Subject { get; }
    public TimeInterval Period { get; private init; }
    public VacationId? Target { get; private init; }
    public string? Comment { get; private init; }
    public string? ResponseComment { get; private init; }
    public VacationRequestStatus Status { get; private init; } = new VacationRequestStatus.Pending();

    public static Result<VacationRequest, ErrorList> Create(VacationRequestId id, VacationSubject subject, TimeInterval period, string? comment)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), subject.Validate());
        if (validation.IsFailure) return validation.Error;
        return new VacationRequest(id, subject, period, Validation.OptionalText(comment));
    }

    public static Result<VacationRequest, ErrorList> CreateChange(VacationRequestId id, Vacation vacation, TimeInterval period, string? comment)
    {
        if (vacation.IsDeleted) return ErrorList.Of(new VacationError.VacationDeleted());
        var request = Create(id, vacation.Subject, period, comment);
        if (request.IsFailure) return request.Error;
        return request.Value with { Target = vacation.Id };
    }

    public Result<VacationRequest, ErrorList> Edit(TimeInterval period, string? comment)
    {
        if (Status is not VacationRequestStatus.Pending) return ErrorList.Of(new VacationError.RequestAlreadyDecided());
        return this with { Period = period, Comment = Validation.OptionalText(comment) };
    }

    public Result<VacationRequest, ErrorList> Withdraw()
    {
        if (Status is not VacationRequestStatus.Pending) return ErrorList.Of(new VacationError.RequestAlreadyDecided());
        return this with { Status = new VacationRequestStatus.Withdrawn() };
    }

    public Result<VacationRequest, ErrorList> Reject(string? comment)
    {
        if (Status is not VacationRequestStatus.Pending) return ErrorList.Of(new VacationError.RequestAlreadyDecided());
        return this with { Status = new VacationRequestStatus.Rejected(), ResponseComment = Validation.OptionalText(comment) };
    }

    public Result<VacationApproval, ErrorList> Approve(VacationId vacationId, string? comment)
    {
        if (Status is not VacationRequestStatus.Pending) return ErrorList.Of(new VacationError.RequestAlreadyDecided());
        if (Target is { } target && target != vacationId) return ErrorList.Of(new VacationError.WrongVacation());
        var vacation = Vacation.Create(vacationId, Subject, Period, Id);
        if (vacation.IsFailure) return vacation.Error;
        var request = this with { Status = new VacationRequestStatus.Approved(vacationId), ResponseComment = Validation.OptionalText(comment) };
        return new VacationApproval(request, vacation.Value);
    }
}

public sealed record SchoolClosure
{
    private SchoolClosure(SchoolClosureId id, TimeInterval period, string reason) { Id = id; Period = period; Reason = reason; }
    public SchoolClosureId Id { get; }
    public TimeInterval Period { get; private init; }
    public string Reason { get; }
    public bool IsDeleted { get; private init; }

    public static Result<SchoolClosure, ErrorList> Create(SchoolClosureId id, TimeInterval period, string? reason)
    {
        var validReason = Validation.Required("reason", reason);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validReason));
        if (validation.IsFailure) return validation.Error;
        return new SchoolClosure(id, period, validReason.Value);
    }

    public Result<SchoolClosure, ErrorList> ChangePeriod(TimeInterval period)
    {
        if (IsDeleted) return ErrorList.Of(new SchoolClosureError.Deleted());
        return this with { Period = period };
    }

    public SchoolClosure Remove() => this with { IsDeleted = true };
}

public closed record AbsenceCause
{
    public sealed record Vacation(VacationId Id) : AbsenceCause;
    public sealed record SchoolClosure(SchoolClosureId Id) : AbsenceCause;
}
