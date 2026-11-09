using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class AppointmentRules
{
    public static Result<Appointment, ErrorList> RecordPastStateChange(Appointment previous, Appointment changed,
        DateTimeOffset now, string? comment, StaffId? authorId)
    {
        if (previous.Id != changed.Id) return ErrorList.Single(new ModelError.WrongOwner());
        if (previous.StartsAt >= now || previous.Status == changed.Status && previous.IsDeleted == changed.IsDeleted) return changed;
        var text = Validation.Required("comment", comment);
        if (text.IsFailure) return text.Error;
        if (authorId is not { } author || author.Value == Guid.Empty) return ErrorList.Single(new ModelError.InvalidValue(), "authorId");
        return changed with { StateHistory = changed.StateHistory.Add(new(author, now.ToUniversalTime(), previous.Status,
            changed.Status, previous.IsDeleted, changed.IsDeleted, text.Value)) };
    }

    public static Result<Appointment, ErrorList> SelectTopic(this Appointment model, CourseEnrollment enrollment, Course course, TopicId? topicId)
    {

        if (model.IsDeleted) return ErrorList.Single(new AppointmentError.Deleted());
        if (enrollment.ClientId != model.ClientId || enrollment.CourseId != course.Id) return ErrorList.Single(new ModelError.WrongOwner());
        if (topicId is null) return model with { Topic = null };
        var topic = course.Topics.FirstOrDefault(item => item.Id == topicId);
        if (topic is null) return ErrorList.Single(new ModelError.NotFound());
        if (!enrollment.IsOpen(topic)) return ErrorList.Single(new LearningError.TopicLocked());
        return model with { Topic = new LessonTopic(enrollment.Id, topic.Id) };
    }

    private static Result<Appointment, ErrorList> CreateScheduled(AppointmentId id, ClientId clientId, ServiceId serviceId, StaffId staffId, RoomId? roomId, DateTimeOffset startsAt, LessonDuration duration, SeriesMembership? series)
    {
        startsAt = startsAt.ToUniversalTime();
        var ids = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"),
            Validation.Identifier(serviceId.Value, "serviceId"), Validation.Identifier(staffId.Value, "staffId"),
            roomId is { } room ? Validation.Identifier(room.Value, "roomId") : UnitResult.Success<ErrorList>());
        var errors = new ErrorListBuilder().Add(ids);
        if (startsAt.Ticks > DateTimeOffset.MaxValue.Ticks - duration.ToTimeSpan().Ticks) errors.Add(new AppointmentError.EndOutOfRange(), "startsAt");
        else if (!SchoolTime.IsWholeHour(startsAt)) errors.Add(new AppointmentError.StartNotOnWholeHour(), "startsAt");
        var validation = errors.ToResult();
        if (validation.IsFailure) return validation.Error;
        return new Appointment(id, clientId, serviceId, staffId, roomId, startsAt, duration, series);
    }

    public static Result<Appointment, ErrorList> Create(AppointmentId id, ClientId clientId, ServiceId serviceId, StaffId staffId, RoomId? roomId, DateTimeOffset startsAt, LessonDuration duration) => CreateScheduled(id, clientId, serviceId, staffId, roomId, startsAt, duration, null);

    public static Result<Appointment, ErrorList> CreateFromSeries(AppointmentId id, Series series, SequenceNumber sequenceNumber, DateTimeOffset startsAt)
    {
        var membership = new SeriesMembership(series.Id, sequenceNumber, [], null);
        return CreateScheduled(id, series.ClientId, series.ServiceId, series.Rule.StaffId, series.Rule.RoomId, startsAt, series.Duration, membership);
    }

    private static Result<Appointment, ErrorList> SetResult(this Appointment model, DateTimeOffset now, AppointmentStatus result)
    {
        if (model.IsDeleted) return ErrorList.Single(new AppointmentError.Deleted());
        if (now < model.EndsAt) return ErrorList.Single(new AppointmentError.ResultInFuture());
        if (model.Status == result) return model;
        if (model.Status != AppointmentStatus.Scheduled) return ErrorList.Single(new AppointmentError.InvalidState());
        return model with { Status = result };
    }

    public static Result<Appointment, ErrorList> Complete(this Appointment model, DateTimeOffset now) => model.SetResult(now, AppointmentStatus.Completed);

    public static Result<Appointment, ErrorList> Burn(this Appointment model, DateTimeOffset now) => model.SetResult(now, AppointmentStatus.Burned);

    public static Result<Appointment, ErrorList> ClearResult(this Appointment model)
    {
        if (model.IsDeleted) return ErrorList.Single(new AppointmentError.Deleted());
        if (model.Status == AppointmentStatus.Scheduled) return model;
        if (model.Status is not (AppointmentStatus.Completed or AppointmentStatus.Burned)) return ErrorList.Single(new AppointmentError.InvalidState());
        return model with { Status = AppointmentStatus.Scheduled };
    }

    private static Result<Appointment, ErrorList> Edit(this Appointment model, Func<Appointment, Appointment> change)
    {
        if (model.IsDeleted) return ErrorList.Single(new AppointmentError.Deleted());
        if (model.Status is AppointmentStatus.Completed or AppointmentStatus.Burned) return ErrorList.Single(new AppointmentError.ClearResultFirst());
        if (model.Status == AppointmentStatus.Cancelled) return ErrorList.Single(new AppointmentError.InvalidState());
        var updated = change(model);
        var validated = CreateScheduled(updated.Id, updated.ClientId, updated.ServiceId, updated.StaffId, updated.RoomId, updated.StartsAt, updated.Duration, updated.Series);
        if (validated.IsFailure) return validated.Error;

        return updated with { StartsAt = validated.Value.StartsAt };
    }

    private static Appointment MarkOverride(this Appointment model, AppointmentOverride field) => model.Series is null
        ? model : model with { Series = model.Series with { Overrides = model.Series.Overrides.Add(field) } };

    public static Result<Appointment, ErrorList> Move(this Appointment model, DateTimeOffset startsAt) => model.Edit(
        current => current.StartsAt == startsAt ? current : (current with { StartsAt = startsAt }).MarkOverride(AppointmentOverride.StartTime));

    public static Result<Appointment, ErrorList> ReplaceStaff(this Appointment model, StaffId staffId) => model.Edit(
        current => current.StaffId == staffId ? current : (current with { StaffId = staffId }).MarkOverride(AppointmentOverride.Staff));

    public static Result<Appointment, ErrorList> ChangeRoom(this Appointment model, RoomId? roomId) => model.Edit(
        current => current.RoomId == roomId ? current : (current with { RoomId = roomId }).MarkOverride(AppointmentOverride.Room));

    public static Result<Appointment, ErrorList> ApplySeriesChange(this Appointment model, SeriesId seriesId, SequenceNumber boundary, AppointmentPlanChange change)
    {
        if (model.Series is not { } membership || membership.SeriesId != seriesId || membership.SequenceNumber.Value < boundary.Value || model.Status != AppointmentStatus.Scheduled)
            return model;
        var selected = membership.SequenceNumber == boundary;
        bool CanChange(AppointmentOverride field) => selected || !membership.Overrides.Contains(field);
        var startsAt = CanChange(AppointmentOverride.StartTime) ? change.StartsAt ?? model.StartsAt : model.StartsAt;
        var staffId = CanChange(AppointmentOverride.Staff) ? change.StaffId ?? model.StaffId : model.StaffId;
        var roomId = change.Room is RoomChange.Set room && CanChange(AppointmentOverride.Room) ? room.RoomId : model.RoomId;
        var overrides = membership.Overrides;
        if (selected)
        {
            if (change.StartsAt.HasValue) overrides = overrides.Remove(AppointmentOverride.StartTime);
            if (change.StaffId.HasValue) overrides = overrides.Remove(AppointmentOverride.Staff);
            if (change.Room is RoomChange.Set) overrides = overrides.Remove(AppointmentOverride.Room);
        }
        return model.Edit(current => current with { StartsAt = startsAt, StaffId = staffId, RoomId = roomId, Series = membership with { Overrides = overrides } });
    }

    public static Result<Appointment, ErrorList> Cancel(this Appointment model)
    {
        if (model.IsDeleted) return ErrorList.Single(new AppointmentError.Deleted());
        if (model.Status is AppointmentStatus.Completed or AppointmentStatus.Burned) return ErrorList.Single(new AppointmentError.ClearResultFirst());
        if (model.Status == AppointmentStatus.Cancelled) return model;
        return model with { Status = AppointmentStatus.Cancelled, CancellationReasons = [new CancellationReason.Manual()] };
    }

    public static Appointment CancelForSeriesEnd(this Appointment model, SeriesId seriesId, DateOnly fromDate)
    {
        if (model.Series?.SeriesId != seriesId || model.IsDeleted || model.Status != AppointmentStatus.Scheduled || SchoolTime.Date(model.StartsAt) < fromDate) return model;
        return model with { Status = AppointmentStatus.Cancelled, CancellationReasons = [new CancellationReason.SeriesEnded()] };
    }

    public static Result<Appointment, ErrorList> Delete(this Appointment model)
    {
        if (model.IsDeleted) return ErrorList.Single(new AppointmentError.Deleted());
        if (model.Status is AppointmentStatus.Completed or AppointmentStatus.Burned) return ErrorList.Single(new AppointmentError.ClearResultFirst());
        return model with { IsDeleted = true };
    }

    public static bool CountsTowardSeriesTarget(this Appointment model) => !model.IsDeleted && (model.Status != AppointmentStatus.Cancelled
        || model.CancellationReasons.SetEquals([new CancellationReason.Manual()]));
}
