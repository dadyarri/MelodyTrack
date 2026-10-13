using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Vacations.Requests;

public sealed class GetVacationsRequest
{
    public string? SubjectType { get; set; }
    public Ulid? SubjectId { get; set; }
    public bool IncludeHistory { get; set; }
}

public sealed class CreateVacationInput : IValidatableRequest
{
    public string? SubjectType { get; set; }
    public Ulid? SubjectId { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Message { get; set; }
    public bool CancelConflictingAppointments { get; set; }
}

public sealed class ChangeVacationInput : IValidatableRequest
{
    public int ExpectedVersion { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string? Message { get; set; }
    public bool CancelConflictingAppointments { get; set; }
}

public sealed class VacationVersionInput : IValidatableRequest
{
    public int ExpectedVersion { get; set; }
    public string? Message { get; set; }
}

public sealed class RestoreVacationAppointmentsInput : IValidatableRequest
{
    public int ExpectedVersion { get; set; }
    public required List<Ulid> InterruptionIds { get; set; }
}
