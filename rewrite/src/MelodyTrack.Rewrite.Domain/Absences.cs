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
    internal Vacation(VacationId id, VacationSubject subject, TimeInterval period, VacationRequestId? requestId)
    {
        Id = id; Subject = subject; Period = period; RequestId = requestId;
    }

    public VacationId Id { get; }

    public VacationSubject Subject { get; }

    public TimeInterval Period { get; internal init; }

    public VacationRequestId? RequestId { get; }

    public bool IsDeleted { get; internal init; }
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
    internal VacationRequest(VacationRequestId id, VacationSubject subject, TimeInterval period, string? comment)
    {
        Id = id; Subject = subject; Period = period; Comment = comment;
    }

    public VacationRequestId Id { get; }

    public VacationSubject Subject { get; }

    public TimeInterval Period { get; internal init; }

    public VacationId? Target { get; internal init; }

    public string? Comment { get; internal init; }

    public string? ResponseComment { get; internal init; }

    public VacationRequestStatus Status { get; internal init; } = new VacationRequestStatus.Pending();
}

public sealed record SchoolClosure
{
    internal SchoolClosure(SchoolClosureId id, TimeInterval period, string reason) { Id = id; Period = period; Reason = reason; }

    public SchoolClosureId Id { get; }

    public TimeInterval Period { get; internal init; }

    public string Reason { get; internal init; }

    public bool IsDeleted { get; internal init; }
}

public closed record AbsenceCause
{
    public sealed record Vacation(VacationId Id) : AbsenceCause;
    public sealed record SchoolClosure(SchoolClosureId Id) : AbsenceCause;
}
