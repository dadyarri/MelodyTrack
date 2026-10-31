using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

public static class WorkingScheduleOperations
{
    public static Result<WorkingSchedule, ErrorList> Approve(this WorkingSchedule model, StaffRole role, bool hasConflictingAppointments, bool hasConflictingSeries)
    {
        if (role != StaffRole.Superuser) return ErrorList.Single(new WorkingScheduleError.Forbidden());
        return WorkingScheduleRules.Approve(model, hasConflictingAppointments, hasConflictingSeries);
    }

    public static Result<WorkingSchedule, ErrorList> Reject(this WorkingSchedule model, StaffRole role)
    {
        if (role != StaffRole.Superuser) return ErrorList.Single(new WorkingScheduleError.Forbidden());
        return WorkingScheduleRules.Reject(model);
    }
}
