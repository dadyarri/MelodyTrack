using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public sealed record SeriesSkip
{
    private SeriesSkip(SeriesId seriesId, SeriesCandidate candidate, string? reason)
    {
        SeriesId = seriesId; SequenceNumber = candidate.SequenceNumber; StartsAt = candidate.StartsAt.ToUniversalTime(); Reason = Validation.OptionalText(reason);
    }
    public SeriesId SeriesId { get; }
    public SequenceNumber SequenceNumber { get; }
    public DateTimeOffset StartsAt { get; }
    public string? Reason { get; }

    public static Result<SeriesSkip, ErrorList> Create(SeriesId seriesId, SeriesCandidate candidate, string? reason)
    {
        var validation = Validation.Identifier(seriesId.Value, "seriesId");
        if (validation.IsFailure) return validation.Error;
        return new SeriesSkip(seriesId, candidate, reason);
    }
}

public closed record ConflictResolution
{
    public sealed record Pending : ConflictResolution;
    public sealed record Created(AppointmentId AppointmentId) : ConflictResolution;
    public sealed record Skipped(SeriesSkip Skip) : ConflictResolution;
}

public sealed record SeriesConflict
{
    private SeriesConflict(Series series, SeriesCandidate candidate)
    {
        SeriesId = series.Id; SequenceNumber = candidate.SequenceNumber; ClientId = series.ClientId; ServiceId = series.ServiceId;
        Duration = series.Duration; DesiredStart = candidate.StartsAt.ToUniversalTime(); StaffId = series.Rule.StaffId; RoomId = series.Rule.RoomId;
    }
    public SeriesId SeriesId { get; }
    public SequenceNumber SequenceNumber { get; }
    public ClientId ClientId { get; }
    public ServiceId ServiceId { get; }
    public LessonDuration Duration { get; }
    public DateTimeOffset DesiredStart { get; }
    public StaffId StaffId { get; }
    public RoomId? RoomId { get; }
    public ConflictResolution Resolution { get; private init; } = new ConflictResolution.Pending();
    public (SeriesId SeriesId, SequenceNumber SequenceNumber) Key => (SeriesId, SequenceNumber);

    public static SeriesConflict Create(Series series, SeriesCandidate candidate) => new(series, candidate);

    public Result<SeriesConflict, ErrorList> ConfirmCreated(StaffRole role, IReadOnlyCollection<AvailabilityConflict> blockers, Appointment appointment)
    {
        if (role == StaffRole.Teacher) return ErrorList.Of(new SeriesConflictError.Forbidden());
        if (appointment.Series is not { } membership || membership.SeriesId != SeriesId || membership.SequenceNumber != SequenceNumber
            || appointment.ClientId != ClientId || appointment.ServiceId != ServiceId || appointment.Duration != Duration
            || appointment.IsDeleted || appointment.Status != AppointmentStatus.Scheduled)
            return ErrorList.Of(new SeriesConflictError.WrongAppointment());
        return Resolution switch
        {
            ConflictResolution.Created created when created.AppointmentId == appointment.Id => this,
            ConflictResolution.Pending when blockers.Count > 0 => ErrorList.Of(new SeriesConflictError.StillBlocked()),
            ConflictResolution.Pending => this with { Resolution = new ConflictResolution.Created(appointment.Id) },
            _ => ErrorList.Of(new SeriesConflictError.AlreadyResolved())
        };
    }

    public Result<SeriesConflict, ErrorList> Skip(StaffRole role, string? reason)
    {
        if (role == StaffRole.Teacher) return ErrorList.Of(new SeriesConflictError.Forbidden());
        if (Resolution is ConflictResolution.Skipped) return this;
        if (Resolution is ConflictResolution.Created) return ErrorList.Of(new SeriesConflictError.AlreadyResolved());
        var skip = SeriesSkip.Create(SeriesId, new(SequenceNumber, DesiredStart), reason);
        if (skip.IsFailure) return skip.Error;
        return this with { Resolution = new ConflictResolution.Skipped(skip.Value) };
    }

    public static ImmutableDictionary<SeriesId, ImmutableArray<SeriesConflict>> Outstanding(IEnumerable<SeriesConflict> conflicts) => conflicts
        .Where(conflict => conflict.Resolution is ConflictResolution.Pending).DistinctBy(conflict => conflict.Key)
        .GroupBy(conflict => conflict.SeriesId).ToImmutableDictionary(group => group.Key, group => group.ToImmutableArray());
}

public sealed record SeriesFinish(Series Series, ImmutableArray<Appointment> Appointments);
public sealed record CompensationRestoration(Appointment Original, Appointment Replacement);

public static class SeriesEffects
{
    public static Result<SeriesFinish, ErrorList> Finish(DateOnly fromDate, Series series, IEnumerable<Appointment> appointments)
    {
        var finished = series.Finish(fromDate);
        if (finished.IsFailure) return finished.Error;
        return new SeriesFinish(finished.Value, appointments.Select(appointment => appointment.CancelForSeriesEnd(series.Id, fromDate)).ToImmutableArray());
    }

    public static int CountedAppointments(Series series, IEnumerable<Appointment> appointments) => appointments
        .Where(appointment => appointment.CountsTowardSeriesTarget && appointment.Series?.SeriesId == series.Id)
        .Select(appointment => appointment.Series!.SequenceNumber).Distinct().Count();

    public static Result<CompensationRestoration, ErrorList> RestoreWithReplacement(StaffRole role, DateTimeOffset now, bool hasBlockers, Appointment original, Appointment replacement)
    {
        if (original.Series is not { } source || replacement.Series is not { } tail || source.SeriesId != tail.SeriesId || tail.Compensates != source.SequenceNumber)
            return ErrorList.Of(new AppointmentError.ReplacementRequiresReview());
        var restored = original.RestoreAfterAbsence(role, now, hasBlockers);
        if (restored.IsFailure) return restored.Error;
        var cancelled = replacement.CancelAsCompensationReplacement(role, now);
        if (cancelled.IsFailure) return cancelled.Error;
        return new CompensationRestoration(restored.Value, cancelled.Value);
    }
}
