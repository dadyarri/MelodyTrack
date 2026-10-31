using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

public static class CourseEnrollmentOperations
{
    public static Result<CourseEnrollment, ErrorList> ChangeStatus(this CourseEnrollment model, Course course, TopicId topicId, TopicStatus status, Actor actor, bool confirmRelocking, IEnumerable<CourseEnrollment> otherEnrollments, bool administratorScopeGranted = false)
    {
        if (!actor.IsSuperuser() && actor is not Actor.Staff { Role: StaffRole.Teacher } &&
            !(actor is Actor.Staff { Role: StaffRole.Administrator } && administratorScopeGranted))
            return ErrorList.Single(new ModelError.Forbidden());
        if (actor is Actor.Staff { Role: StaffRole.Teacher } teacher && !model.Teachers.Contains(teacher.Id))
            return ErrorList.Single(new ModelError.Forbidden());
        return CourseEnrollmentRules.ChangeStatus(model, course, topicId, status, confirmRelocking, otherEnrollments);
    }
}

public static class PersonalAssignmentOperations
{
    private static bool CanEdit(Actor actor, Appointment appointment) => actor.IsSuperuser() ||
        actor is Actor.Staff { Role: StaffRole.Teacher } teacher && teacher.Id == appointment.StaffId;

    public static Result<PersonalAssignment, ErrorList> Create(PersonalAssignmentId id, Appointment appointment, LearningDocument content, Actor actor)
    {
        if (!CanEdit(actor, appointment)) return ErrorList.Single(new ModelError.Forbidden());
        return PersonalAssignmentRules.Create(id, appointment, content);
    }

    public static Result<PersonalAssignment, ErrorList> Edit(this PersonalAssignment model, Appointment appointment, LearningDocument content, Actor actor)
    {
        if (appointment.Id != model.AppointmentId || appointment.ClientId != model.ClientId) return ErrorList.Single(new ModelError.WrongOwner());
        if (!CanEdit(actor, appointment)) return ErrorList.Single(new ModelError.Forbidden());
        return PersonalAssignmentRules.Edit(model, appointment, content);
    }

    public static Result<PersonalAssignment, ErrorList> SetCompleted(this PersonalAssignment model, Appointment appointment, bool completed, Actor actor)
    {
        if (appointment.Id != model.AppointmentId || appointment.ClientId != model.ClientId) return ErrorList.Single(new ModelError.WrongOwner());
        if (!CanEdit(actor, appointment)) return ErrorList.Single(new ModelError.Forbidden());
        return PersonalAssignmentRules.SetCompleted(model, appointment, completed);
    }
}
