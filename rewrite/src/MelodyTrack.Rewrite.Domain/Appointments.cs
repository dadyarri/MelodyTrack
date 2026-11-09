using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public enum AppointmentStatus { Scheduled, Completed, Burned, Cancelled }
public enum AppointmentOverride { StartTime, Staff, Room }

public closed record CancellationReason
{
    public sealed record Manual : CancellationReason;
    public sealed record Absence(AbsenceCause Cause) : CancellationReason;
    public sealed record SeriesEnded : CancellationReason;
    public sealed record CompensationReplaced : CancellationReason;
}

public closed record RoomChange
{
    public sealed record Keep : RoomChange;
    public sealed record Set(RoomId? RoomId) : RoomChange;
}

public sealed record AppointmentPlanChange(DateTimeOffset? StartsAt, StaffId? StaffId, RoomChange Room);
public sealed record SeriesMembership(SeriesId SeriesId, SequenceNumber SequenceNumber, ImmutableHashSet<AppointmentOverride> Overrides, SequenceNumber? Compensates);
public sealed record AppointmentStateChange(StaffId AuthorId, DateTimeOffset OccurredAt, AppointmentStatus Before,
    AppointmentStatus After, bool WasDeleted, bool IsDeleted, string Comment);

public sealed record Appointment
{
    internal Appointment(AppointmentId id, ClientId clientId, ServiceId serviceId, StaffId staffId, RoomId? roomId,
        DateTimeOffset startsAt, LessonDuration duration, SeriesMembership? series)
    {
        Id = id; ClientId = clientId; ServiceId = serviceId; StaffId = staffId; RoomId = roomId; StartsAt = startsAt; Duration = duration; Series = series;
    }

    public AppointmentId Id { get; }

    public ClientId ClientId { get; }

    public ServiceId ServiceId { get; }

    public StaffId StaffId { get; internal init; }

    public RoomId? RoomId { get; internal init; }

    public DateTimeOffset StartsAt { get; internal init; }

    public LessonDuration Duration { get; }

    public DateTimeOffset EndsAt => StartsAt.Add(Duration.ToTimeSpan());

    public TimeInterval Interval => TimeInterval.Create(StartsAt, EndsAt).Value;

    public AppointmentStatus Status { get; internal init; } = AppointmentStatus.Scheduled;

    public SeriesMembership? Series { get; internal init; }

    public ImmutableHashSet<CancellationReason> CancellationReasons { get; internal init; } = [];

    public bool IsDeleted { get; internal init; }

    public LessonTopic? Topic { get; internal init; }

    public ImmutableArray<AppointmentStateChange> StateHistory { get; internal init; } = [];
}
