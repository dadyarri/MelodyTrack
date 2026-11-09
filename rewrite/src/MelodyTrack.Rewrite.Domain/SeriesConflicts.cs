using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Domain;

public sealed record SeriesSkip
{
    internal SeriesSkip(SeriesId seriesId, SeriesCandidate candidate, string? reason)
    {
        SeriesId = seriesId; SequenceNumber = candidate.SequenceNumber; StartsAt = candidate.StartsAt.ToUniversalTime(); Reason = Validation.OptionalText(reason);
    }

    public SeriesId SeriesId { get; }

    public SequenceNumber SequenceNumber { get; }

    public DateTimeOffset StartsAt { get; }

    public string? Reason { get; }
}

public closed record ConflictResolution
{
    public sealed record Pending : ConflictResolution;
    public sealed record Created(AppointmentId AppointmentId) : ConflictResolution;
    public sealed record Skipped(SeriesSkip Skip) : ConflictResolution;
}

public sealed record SeriesConflict
{
    internal SeriesConflict(Series series, SeriesCandidate candidate)
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

    public ConflictResolution Resolution { get; internal init; } = new ConflictResolution.Pending();

    public (SeriesId SeriesId, SequenceNumber SequenceNumber) Key => (SeriesId, SequenceNumber);
}

public sealed record SeriesFinish(Series Series, ImmutableArray<Appointment> Appointments);

public static class SeriesEffects
{
    public static Result<SeriesFinish, ErrorList> Finish(DateOnly fromDate, Series series, IEnumerable<Appointment> appointments)
    {
        var finished = series.Finish(fromDate);
        if (finished.IsFailure) return finished.Error;
        return new SeriesFinish(finished.Value, appointments.Select(appointment => appointment.CancelForSeriesEnd(series.Id, fromDate)).ToImmutableArray());
    }

    public static int CountedAppointments(Series series, IEnumerable<Appointment> appointments) => appointments
        .Where(appointment => appointment.CountsTowardSeriesTarget() && appointment.Series?.SeriesId == series.Id)
        .Select(appointment => appointment.Series!.SequenceNumber).Distinct().Count();


}
