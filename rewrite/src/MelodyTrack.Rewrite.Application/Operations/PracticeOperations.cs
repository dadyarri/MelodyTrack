using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

public static class PracticeAttemptOperations
{
    private static bool SameAccount(Actor left, Actor right) => (left, right) switch
    {
        (Actor.Client first, Actor.Client second) => first.Id == second.Id,
        (Actor.Staff first, Actor.Staff second) => first.Id == second.Id,
        _ => false
    };

    public static bool CanRead(this PracticeAttempt model, Actor actor, CourseEnrollment? enrollment = null, Appointment? appointment = null) => model.Context switch
    {
        PracticeContext.Free free => SameAccount(free.Owner, actor),
        PracticeContext.Course course => actor.IsSuperuser() || actor is Actor.Client client && client.Id == course.ClientId ||
            actor is Actor.Staff { Role: StaffRole.Teacher } teacher && enrollment is not null &&
            enrollment.Id == course.EnrollmentId && enrollment.ClientId == course.ClientId && enrollment.Teachers.Contains(teacher.Id),
        PracticeContext.PersonalHomework homework => actor.IsSuperuser() || actor is Actor.Client client && client.Id == homework.ClientId ||
            actor is Actor.Staff { Role: StaffRole.Teacher } teacher && appointment is not null &&
            appointment.Id == homework.AppointmentId && appointment.ClientId == homework.ClientId && appointment.StaffId == teacher.Id
    };

    public static Result<TrainerConfiguration, ErrorList> CopyFreeConfiguration(this PracticeAttempt model, Actor actor, bool trainerIsRunning)
    {
        if (model.Context is not PracticeContext.Free || !model.CanRead(actor)) return ErrorList.Single(new ModelError.Forbidden());
        return PracticeAttemptRules.CopyFreeConfiguration(model, trainerIsRunning);
    }

    public static Result<TimeSpan, ErrorList> VideoDuration(this PracticeAttempt model, Actor actor, TimeSpan? loopDurationOverride = null, CourseEnrollment? enrollment = null, Appointment? appointment = null)
    {
        if (actor is not Actor.Staff || !model.CanRead(actor, enrollment, appointment)) return ErrorList.Single(new ModelError.Forbidden());
        return PracticeAttemptRules.VideoDuration(model, loopDurationOverride);
    }
}
