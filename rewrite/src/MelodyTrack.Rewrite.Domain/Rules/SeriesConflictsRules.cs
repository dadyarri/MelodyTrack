using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class SeriesSkipRules
{
    public static Result<SeriesSkip, ErrorList> Create(SeriesId seriesId, SeriesCandidate candidate, string? reason)
    {
        var validation = Validation.Identifier(seriesId.Value, "seriesId");
        if (validation.IsFailure) return validation.Error;
        return new SeriesSkip(seriesId, candidate, reason);
    }
}

public static class SeriesConflictRules
{
    public static SeriesConflict Create(Series series, SeriesCandidate candidate) => new(series, candidate);

    public static Result<SeriesConflict, ErrorList> ConfirmCreated(this SeriesConflict model, IReadOnlyCollection<AvailabilityConflict> blockers, Appointment appointment)
    {

        if (appointment.Series is not { } membership || membership.SeriesId != model.SeriesId || membership.SequenceNumber != model.SequenceNumber
            || appointment.ClientId != model.ClientId || appointment.ServiceId != model.ServiceId || appointment.Duration != model.Duration
            || appointment.IsDeleted || appointment.Status != AppointmentStatus.Scheduled)
            return ErrorList.Single(new SeriesConflictError.WrongAppointment());
        return model.Resolution switch
        {
            ConflictResolution.Created created when created.AppointmentId == appointment.Id => model,
            ConflictResolution.Pending when blockers.Count > 0 => ErrorList.Single(new SeriesConflictError.StillBlocked()),
            ConflictResolution.Pending => model with { Resolution = new ConflictResolution.Created(appointment.Id) },
            ConflictResolution.Created => ErrorList.Single(new SeriesConflictError.AlreadyResolved()),
            ConflictResolution.Skipped => ErrorList.Single(new SeriesConflictError.AlreadyResolved())
        };
    }

    public static Result<SeriesConflict, ErrorList> Skip(this SeriesConflict model, string? reason)
    {

        if (model.Resolution is ConflictResolution.Skipped) return model;
        if (model.Resolution is ConflictResolution.Created) return ErrorList.Single(new SeriesConflictError.AlreadyResolved());
        var skip = SeriesSkipRules.Create(model.SeriesId, new(model.SequenceNumber, model.DesiredStart), reason);
        if (skip.IsFailure) return skip.Error;
        return model with { Resolution = new ConflictResolution.Skipped(skip.Value) };
    }

    public static ImmutableDictionary<SeriesId, ImmutableArray<SeriesConflict>> Outstanding(IEnumerable<SeriesConflict> conflicts) => conflicts
        .Where(conflict => conflict.Resolution is ConflictResolution.Pending).DistinctBy(conflict => conflict.Key)
        .GroupBy(conflict => conflict.SeriesId).ToImmutableDictionary(group => group.Key, group => group.ToImmutableArray());
}
