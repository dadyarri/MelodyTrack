using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

public static class SeriesConflictOperations
{
    public static Result<SeriesConflict, ErrorList> ConfirmCreated(this SeriesConflict model, StaffRole role, IReadOnlyCollection<AvailabilityConflict> blockers, Appointment appointment)
    {
        if (role == StaffRole.Teacher) return ErrorList.Single(new SeriesConflictError.Forbidden());
        return SeriesConflictRules.ConfirmCreated(model, blockers, appointment);
    }

    public static Result<SeriesConflict, ErrorList> Skip(this SeriesConflict model, StaffRole role, string? reason)
    {
        if (role == StaffRole.Teacher) return ErrorList.Single(new SeriesConflictError.Forbidden());
        return SeriesConflictRules.Skip(model, reason);
    }
}
