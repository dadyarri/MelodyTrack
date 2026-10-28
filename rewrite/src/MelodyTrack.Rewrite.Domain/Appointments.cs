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

public sealed record Appointment
{
    private Appointment(AppointmentId id, ClientId clientId, ServiceId serviceId, StaffId staffId, RoomId? roomId,
        DateTimeOffset startsAt, LessonDuration duration, SeriesMembership? series)
    {
        Id = id; ClientId = clientId; ServiceId = serviceId; StaffId = staffId; RoomId = roomId; StartsAt = startsAt; Duration = duration; Series = series;
    }
    public AppointmentId Id { get; }
    public ClientId ClientId { get; }
    public ServiceId ServiceId { get; }
    public StaffId StaffId { get; private init; }
    public RoomId? RoomId { get; private init; }
    public DateTimeOffset StartsAt { get; private init; }
    public LessonDuration Duration { get; }
    public DateTimeOffset EndsAt => StartsAt.Add(Duration.ToTimeSpan());
    public TimeInterval Interval => TimeInterval.Create(StartsAt, EndsAt).Value;
    public AppointmentStatus Status { get; private init; } = AppointmentStatus.Scheduled;
    public SeriesMembership? Series { get; private init; }
    public ImmutableHashSet<CancellationReason> CancellationReasons { get; private init; } = [];
    public bool IsDeleted { get; private init; }
    public bool CountsTowardSeriesTarget => !IsDeleted && (Status != AppointmentStatus.Cancelled
        || CancellationReasons.SetEquals([new CancellationReason.Manual()]));

    private static Result<Appointment, ErrorList> CreateScheduled(AppointmentId id, ClientId clientId, ServiceId serviceId, StaffId staffId,
        RoomId? roomId, DateTimeOffset startsAt, LessonDuration duration, SeriesMembership? series)
    {
        startsAt = startsAt.ToUniversalTime();
        var ids = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"),
            Validation.Identifier(serviceId.Value, "serviceId"), Validation.Identifier(staffId.Value, "staffId"),
            roomId is { } room ? Validation.Identifier(room.Value, "roomId") : UnitResult.Success<ErrorList>());
        var errors = ids.IsFailure ? ids.Error.Issues.ToList() : [];
        if (startsAt.Ticks > DateTimeOffset.MaxValue.Ticks - duration.ToTimeSpan().Ticks) errors.Add(new(new AppointmentError.EndOutOfRange(), "startsAt"));
        else if (!SchoolTime.IsWholeHour(startsAt)) errors.Add(new(new AppointmentError.StartNotOnWholeHour(), "startsAt"));
        if (errors.Count > 0) return ErrorList.From(errors);
        return new Appointment(id, clientId, serviceId, staffId, roomId, startsAt, duration, series);
    }

    public static Result<Appointment, ErrorList> Create(AppointmentId id, ClientId clientId, ServiceId serviceId, StaffId staffId,
        RoomId? roomId, DateTimeOffset startsAt, LessonDuration duration) => CreateScheduled(id, clientId, serviceId, staffId, roomId, startsAt, duration, null);

    public static Result<Appointment, ErrorList> CreateFromSeries(AppointmentId id, Series series, SequenceNumber sequenceNumber, DateTimeOffset startsAt)
    {
        var membership = new SeriesMembership(series.Id, sequenceNumber, [], null);
        return CreateScheduled(id, series.ClientId, series.ServiceId, series.Rule.StaffId, series.Rule.RoomId, startsAt, series.Duration, membership);
    }

    public static Result<Appointment, ErrorList> CreateCompensation(AppointmentId id, Series series, SequenceNumber sequenceNumber, DateTimeOffset startsAt, Appointment original)
    {
        if (series.End is not SeriesEnd.AfterAppointments || original.Series is not { } membership || membership.SeriesId != series.Id
            || sequenceNumber.Value <= membership.SequenceNumber.Value || original.Status != AppointmentStatus.Cancelled
            || !original.HasOnlyAbsenceReasons || original.IsDeleted || original.ClientId != series.ClientId
            || original.ServiceId != series.ServiceId || original.Duration != series.Duration)
            return ErrorList.Of(new AppointmentError.CompensationNotNeeded());

        var result = CreateFromSeries(id, series, sequenceNumber, startsAt);
        if (result.IsFailure) return result.Error;
        var appointment = result.Value;
        return appointment with { Series = appointment.Series! with { Compensates = membership.SequenceNumber } };
    }

    private bool HasOnlyAbsenceReasons => CancellationReasons.Count > 0 && CancellationReasons.All(reason => reason is CancellationReason.Absence);

    private UnitResult<ErrorList> Permission(StaffRole role, DateTimeOffset now)
    {
        if (IsDeleted) return UnitResult.Failure(ErrorList.Of(new AppointmentError.Deleted()));
        if (role == StaffRole.Teacher) return UnitResult.Failure(ErrorList.Of(new AppointmentError.Forbidden()));
        if (role != StaffRole.Superuser && StartsAt < now && !SchoolTime.SameMonth(StartsAt, now))
            return UnitResult.Failure(ErrorList.Of(new AppointmentError.OutsideEditableMonth()));
        return UnitResult.Success<ErrorList>();
    }

    private Result<Appointment, ErrorList> SetResult(StaffRole role, DateTimeOffset now, AppointmentStatus result)
    {
        var permission = Permission(role, now);
        if (permission.IsFailure) return permission.Error;
        if (now < EndsAt) return ErrorList.Of(new AppointmentError.ResultInFuture());
        if (Status == result) return this;
        if (Status != AppointmentStatus.Scheduled) return ErrorList.Of(new AppointmentError.InvalidState());
        return this with { Status = result };
    }

    public Result<Appointment, ErrorList> Complete(StaffRole role, DateTimeOffset now) => SetResult(role, now, AppointmentStatus.Completed);
    public Result<Appointment, ErrorList> Burn(StaffRole role, DateTimeOffset now) => SetResult(role, now, AppointmentStatus.Burned);

    public Result<Appointment, ErrorList> ClearResult(StaffRole role, DateTimeOffset now)
    {
        var permission = Permission(role, now);
        if (permission.IsFailure) return permission.Error;
        if (Status == AppointmentStatus.Scheduled) return this;
        if (Status is not (AppointmentStatus.Completed or AppointmentStatus.Burned)) return ErrorList.Of(new AppointmentError.InvalidState());
        return this with { Status = AppointmentStatus.Scheduled };
    }

    private Result<Appointment, ErrorList> Edit(StaffRole role, DateTimeOffset now, Func<Appointment, Appointment> change)
    {
        var permission = Permission(role, now);
        if (permission.IsFailure) return permission.Error;
        if (Status is AppointmentStatus.Completed or AppointmentStatus.Burned) return ErrorList.Of(new AppointmentError.ClearResultFirst());
        if (Status == AppointmentStatus.Cancelled) return ErrorList.Of(new AppointmentError.InvalidState());
        var updated = change(this);
        var validated = CreateScheduled(updated.Id, updated.ClientId, updated.ServiceId, updated.StaffId, updated.RoomId, updated.StartsAt, updated.Duration, updated.Series);
        if (validated.IsFailure) return validated.Error;
        var targetPermission = validated.Value.Permission(role, now);
        if (targetPermission.IsFailure) return targetPermission.Error;
        return updated with { StartsAt = validated.Value.StartsAt };
    }

    private Appointment MarkOverride(AppointmentOverride field) => Series is null
        ? this : this with { Series = Series with { Overrides = Series.Overrides.Add(field) } };

    public Result<Appointment, ErrorList> Move(StaffRole role, DateTimeOffset now, DateTimeOffset startsAt) => Edit(role, now,
        current => current.StartsAt == startsAt ? current : (current with { StartsAt = startsAt }).MarkOverride(AppointmentOverride.StartTime));

    public Result<Appointment, ErrorList> ReplaceStaff(StaffRole role, DateTimeOffset now, StaffId staffId) => Edit(role, now,
        current => current.StaffId == staffId ? current : (current with { StaffId = staffId }).MarkOverride(AppointmentOverride.Staff));

    public Result<Appointment, ErrorList> ChangeRoom(StaffRole role, DateTimeOffset now, RoomId? roomId) => Edit(role, now,
        current => current.RoomId == roomId ? current : (current with { RoomId = roomId }).MarkOverride(AppointmentOverride.Room));

    public Result<Appointment, ErrorList> ApplySeriesChange(StaffRole role, DateTimeOffset now, SeriesId seriesId, SequenceNumber boundary, AppointmentPlanChange change)
    {
        if (Series is not { } membership || membership.SeriesId != seriesId || membership.SequenceNumber.Value < boundary.Value || Status != AppointmentStatus.Scheduled)
            return this;
        var selected = membership.SequenceNumber == boundary;
        bool CanChange(AppointmentOverride field) => selected || !membership.Overrides.Contains(field);
        var startsAt = CanChange(AppointmentOverride.StartTime) ? change.StartsAt ?? StartsAt : StartsAt;
        var staffId = CanChange(AppointmentOverride.Staff) ? change.StaffId ?? StaffId : StaffId;
        var roomId = change.Room is RoomChange.Set room && CanChange(AppointmentOverride.Room) ? room.RoomId : RoomId;
        var overrides = membership.Overrides;
        if (selected)
        {
            if (change.StartsAt.HasValue) overrides = overrides.Remove(AppointmentOverride.StartTime);
            if (change.StaffId.HasValue) overrides = overrides.Remove(AppointmentOverride.Staff);
            if (change.Room is RoomChange.Set) overrides = overrides.Remove(AppointmentOverride.Room);
        }
        return Edit(role, now, current => current with { StartsAt = startsAt, StaffId = staffId, RoomId = roomId, Series = membership with { Overrides = overrides } });
    }

    public Result<Appointment, ErrorList> Cancel(StaffRole role, DateTimeOffset now)
    {
        var permission = Permission(role, now);
        if (permission.IsFailure) return permission.Error;
        if (Status is AppointmentStatus.Completed or AppointmentStatus.Burned) return ErrorList.Of(new AppointmentError.ClearResultFirst());
        if (Status == AppointmentStatus.Cancelled) return this;
        return this with { Status = AppointmentStatus.Cancelled, CancellationReasons = [new CancellationReason.Manual()] };
    }

    public Appointment CancelForAbsence(AbsenceCause cause)
    {
        if (IsDeleted || Status is AppointmentStatus.Completed or AppointmentStatus.Burned || CancellationReasons.Any(reason => reason is not CancellationReason.Absence)) return this;
        return this with { Status = AppointmentStatus.Cancelled, CancellationReasons = CancellationReasons.Add(new CancellationReason.Absence(cause)) };
    }

    public Result<Appointment, ErrorList> RestoreAfterAbsence(StaffRole role, DateTimeOffset now, bool hasBlockers)
    {
        var permission = Permission(role, now);
        if (permission.IsFailure) return permission.Error;
        if (Status != AppointmentStatus.Cancelled || !HasOnlyAbsenceReasons) return ErrorList.Of(new AppointmentError.InvalidState());
        if (hasBlockers) return ErrorList.Of(new AppointmentError.RestorationBlocked());
        return this with { Status = AppointmentStatus.Scheduled, CancellationReasons = [] };
    }

    public Appointment CancelForSeriesEnd(SeriesId seriesId, DateOnly fromDate)
    {
        if (Series?.SeriesId != seriesId || IsDeleted || Status != AppointmentStatus.Scheduled || SchoolTime.Date(StartsAt) < fromDate) return this;
        return this with { Status = AppointmentStatus.Cancelled, CancellationReasons = [new CancellationReason.SeriesEnded()] };
    }

    public Result<Appointment, ErrorList> Delete(StaffRole role, DateTimeOffset now)
    {
        var permission = Permission(role, now);
        if (permission.IsFailure) return permission.Error;
        if (Status is AppointmentStatus.Completed or AppointmentStatus.Burned) return ErrorList.Of(new AppointmentError.ClearResultFirst());
        return this with { IsDeleted = true };
    }

    internal Result<Appointment, ErrorList> CancelAsCompensationReplacement(StaffRole role, DateTimeOffset now)
    {
        var permission = Permission(role, now);
        if (permission.IsFailure) return permission.Error;
        if (Status != AppointmentStatus.Scheduled || Series?.Compensates is null || Series.Overrides.Count > 0)
            return ErrorList.Of(new AppointmentError.ReplacementRequiresReview());
        return this with { Status = AppointmentStatus.Cancelled, CancellationReasons = [new CancellationReason.CompensationReplaced()] };
    }
}
