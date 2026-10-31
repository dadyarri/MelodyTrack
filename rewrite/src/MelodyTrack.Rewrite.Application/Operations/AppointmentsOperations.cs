using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

public static class AppointmentOperations
{
    public static Result<Appointment, ErrorList> SelectTopic(this Appointment model, CourseEnrollment enrollment, Course course, TopicId? topicId, Actor actor)
    {
        if (!actor.IsSuperuser() && !(actor is Actor.Staff { Role: StaffRole.Teacher } teacher && teacher.Id == model.StaffId))
            return ErrorList.Single(new ModelError.Forbidden());
        return AppointmentRules.SelectTopic(model, enrollment, course, topicId);
    }

    private static UnitResult<ErrorList> Permission(this Appointment model, StaffRole role, DateTimeOffset now)
    {
        if (model.IsDeleted) return UnitResult.Failure(ErrorList.Single(new AppointmentError.Deleted()));
        if (role == StaffRole.Teacher) return UnitResult.Failure(ErrorList.Single(new AppointmentError.Forbidden()));
        if (role != StaffRole.Superuser && model.StartsAt < now && !SchoolTime.SameMonth(model.StartsAt, now))
            return UnitResult.Failure(ErrorList.Single(new AppointmentError.OutsideEditableMonth()));
        return UnitResult.Success<ErrorList>();
    }

    public static Result<Appointment, ErrorList> Complete(this Appointment model, StaffRole role, DateTimeOffset now)
    {
        var permission = Permission(model, role, now);
        if (permission.IsFailure) return permission.Error;
        return AppointmentRules.Complete(model, now);
    }

    public static Result<Appointment, ErrorList> Burn(this Appointment model, StaffRole role, DateTimeOffset now)
    {
        var permission = Permission(model, role, now);
        if (permission.IsFailure) return permission.Error;
        return AppointmentRules.Burn(model, now);
    }

    public static Result<Appointment, ErrorList> ClearResult(this Appointment model, StaffRole role, DateTimeOffset now)
    {
        var permission = Permission(model, role, now);
        if (permission.IsFailure) return permission.Error;
        return AppointmentRules.ClearResult(model);
    }

    public static Result<Appointment, ErrorList> Move(this Appointment model, StaffRole role, DateTimeOffset now, DateTimeOffset startsAt)
    {
        var permission = Permission(model, role, now);
        if (permission.IsFailure) return permission.Error;
        var changed = AppointmentRules.Move(model, startsAt);
        if (changed.IsFailure) return changed.Error;
        var targetPermission = Permission(changed.Value, role, now);
        return targetPermission.IsFailure ? targetPermission.Error : changed.Value;
    }

    public static Result<Appointment, ErrorList> ReplaceStaff(this Appointment model, StaffRole role, DateTimeOffset now, StaffId staffId)
    {
        var permission = Permission(model, role, now);
        if (permission.IsFailure) return permission.Error;
        var changed = AppointmentRules.ReplaceStaff(model, staffId);
        if (changed.IsFailure) return changed.Error;
        var targetPermission = Permission(changed.Value, role, now);
        return targetPermission.IsFailure ? targetPermission.Error : changed.Value;
    }

    public static Result<Appointment, ErrorList> ChangeRoom(this Appointment model, StaffRole role, DateTimeOffset now, RoomId? roomId)
    {
        var permission = Permission(model, role, now);
        if (permission.IsFailure) return permission.Error;
        var changed = AppointmentRules.ChangeRoom(model, roomId);
        if (changed.IsFailure) return changed.Error;
        var targetPermission = Permission(changed.Value, role, now);
        return targetPermission.IsFailure ? targetPermission.Error : changed.Value;
    }

    public static Result<Appointment, ErrorList> ApplySeriesChange(this Appointment model, StaffRole role, DateTimeOffset now, SeriesId seriesId, SequenceNumber boundary, AppointmentPlanChange change)
    {
        if (model.Series is not { } membership || membership.SeriesId != seriesId || membership.SequenceNumber.Value < boundary.Value
            || model.Status != AppointmentStatus.Scheduled) return model;
        var permission = Permission(model, role, now);
        if (permission.IsFailure) return permission.Error;
        var changed = AppointmentRules.ApplySeriesChange(model, seriesId, boundary, change);
        if (changed.IsFailure) return changed.Error;
        var targetPermission = Permission(changed.Value, role, now);
        return targetPermission.IsFailure ? targetPermission.Error : changed.Value;
    }

    public static Result<Appointment, ErrorList> Cancel(this Appointment model, StaffRole role, DateTimeOffset now)
    {
        var permission = Permission(model, role, now);
        if (permission.IsFailure) return permission.Error;
        return AppointmentRules.Cancel(model);
    }

    public static Result<Appointment, ErrorList> RestoreAfterAbsence(this Appointment model, StaffRole role, DateTimeOffset now, bool hasBlockers)
    {
        var permission = Permission(model, role, now);
        if (permission.IsFailure) return permission.Error;
        return AppointmentRules.RestoreAfterAbsence(model, hasBlockers);
    }

    public static Result<Appointment, ErrorList> Delete(this Appointment model, StaffRole role, DateTimeOffset now)
    {
        var permission = Permission(model, role, now);
        if (permission.IsFailure) return permission.Error;
        return AppointmentRules.Delete(model);
    }

    internal static Result<Appointment, ErrorList> CancelAsCompensationReplacement(this Appointment model, StaffRole role, DateTimeOffset now)
    {
        var permission = Permission(model, role, now);
        if (permission.IsFailure) return permission.Error;
        return AppointmentRules.CancelAsCompensationReplacement(model);
    }
}
