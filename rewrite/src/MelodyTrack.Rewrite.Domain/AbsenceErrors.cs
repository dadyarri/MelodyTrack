namespace MelodyTrack.Rewrite.Domain;

public closed record AbsenceError(string Code) : DomainError(Code)
{
    public sealed record ConflictingAppointments() : AbsenceError("absence.conflicting_appointments");
}
