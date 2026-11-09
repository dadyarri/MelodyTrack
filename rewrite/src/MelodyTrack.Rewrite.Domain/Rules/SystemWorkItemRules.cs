using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class SystemWorkItemRules
{
    public static TimeOnly DefaultMorningTime => new(8, 0);

    public static Result<WorkItem?, ErrorList> EnsureLessonReminder(WorkItemId id, TaskTemplate template, Appointment appointment,
        Service service, TimeSpan lead, IEnumerable<WorkItem> existing, DateTimeOffset now)
    {
        if (service.Id != appointment.ServiceId) return ErrorList.Single(new ModelError.InvalidValue(), "serviceId");
        if (lead < TimeSpan.Zero) return ErrorList.Single(new ModelError.InvalidValue(), "lead");
        var kind = service.IsConsultation ? ReminderKind.Consultation : ReminderKind.Appointment;
        var matching = existing.Where(item => item.Source is WorkItemSource.LessonReminder reminder && reminder.AppointmentId == appointment.Id && reminder.Kind == kind).ToArray();
        if (matching.Length > 1) return ErrorList.Single(new ModelError.InvalidState(), "existingTasks");
        if (matching.Length == 1)
        {
            var updated = matching[0].RecalculateReminder(appointment, lead, now);
            return updated.IsFailure ? updated.Error : (WorkItem?)updated.Value;
        }
        if (appointment.IsDeleted || appointment.Status != AppointmentStatus.Scheduled) return (WorkItem?)null;
        try
        {
            var due = appointment.StartsAt - lead;
            var created = WorkItemRules.FromTemplate(id, template, new WorkItemSource.LessonReminder(appointment.Id, kind), due < now ? now : due);
            return created.IsFailure ? created.Error : (WorkItem?)created.Value;
        }
        catch (ArgumentOutOfRangeException) { return ErrorList.Single(new TimeError.OutOfRange()); }
    }

    public static Result<WorkItem?, ErrorList> EnsureBirthday(WorkItemId id, TaskTemplate template, Client client, DateOnly day,
        IEnumerable<WorkItem> existing, DateTimeOffset now, TimeOnly morningTime)
    {
        if (client.Profile.Birthdate is not { } birthdate) return (WorkItem?)null;
        // A non-leap-year replacement date for 29 February is not an approved rule.
        if (birthdate.Month == 2 && birthdate.Day == 29 && !DateTime.IsLeapYear(day.Year))
            return ErrorList.Single(new ModelError.DecisionRequired(), "leapDayBirthday");
        if (birthdate.Month != day.Month || birthdate.Day != day.Day) return (WorkItem?)null;
        var matching = existing.Where(item => item.Source is WorkItemSource.Birthday source && source.ClientId == client.Id && source.Date == day).ToArray();
        if (matching.Length > 1) return ErrorList.Single(new ModelError.InvalidState(), "existingTasks");
        if (matching.Length == 1) return (WorkItem?)matching[0];
        var due = SchoolTime.At(day, morningTime);
        if (due.IsFailure) return due.Error;
        if (now < due.Value) return (WorkItem?)null;
        var created = WorkItemRules.FromTemplate(id, template, new WorkItemSource.Birthday(client.Id, day), due.Value);
        return created.IsFailure ? created.Error : (WorkItem?)created.Value;
    }

    public static Result<WorkItem?, ErrorList> EnsureTeacherSchedule(WorkItemId id, TaskTemplate template, Staff staff, DateOnly day,
        IEnumerable<Appointment> appointments, IEnumerable<WorkItem> existing, DateTimeOffset now, TimeOnly morningTime)
    {
        var matching = existing.Where(item => item.Source is WorkItemSource.TeacherSchedule source && source.StaffId == staff.Id && source.Date == day).ToArray();
        if (matching.Length > 1) return ErrorList.Single(new ModelError.InvalidState(), "existingTasks");
        var included = appointments.Where(item => item.StaffId == staff.Id && SchoolTime.Date(item.StartsAt) == day && !item.IsDeleted && item.Status != AppointmentStatus.Cancelled)
            .Select(item => item.Id).ToImmutableHashSet();
        if (matching.Length == 1)
        {
            var current = matching[0];
            return (WorkItem?)(current.IsPending ? current with { Source = new WorkItemSource.TeacherSchedule(staff.Id, day, included) } : current);
        }
        if (included.IsEmpty) return (WorkItem?)null;
        var due = SchoolTime.At(day, morningTime);
        if (due.IsFailure) return due.Error;
        if (now < due.Value) return (WorkItem?)null;
        var created = WorkItemRules.FromTemplate(id, template, new WorkItemSource.TeacherSchedule(staff.Id, day, included), due.Value);
        return created.IsFailure ? created.Error : (WorkItem?)created.Value;
    }

    public static Result<WorkItem?, ErrorList> EnsureMissingResults(WorkItemId id, TaskTemplate template, DateOnly day,
        IEnumerable<Appointment> appointments, IEnumerable<WorkItem> existing, DateTimeOffset now, TimeOnly morningTime)
    {
        var meetings = appointments.DistinctBy(item => item.Id).ToArray();
        var matching = existing.Where(item => item.Source is WorkItemSource.MissingResults source && source.Date == day).ToArray();
        if (matching.Length > 1) return ErrorList.Single(new ModelError.InvalidState(), "existingTasks");
        if (matching.Length == 1)
        {
            var refreshed = RefreshMissingResults(matching[0], meetings);
            return refreshed.IsFailure ? refreshed.Error : (WorkItem?)refreshed.Value;
        }

        if (day == DateOnly.MaxValue) return ErrorList.Single(new TimeError.OutOfRange());
        var due = SchoolTime.At(day.AddDays(1), morningTime);
        if (due.IsFailure) return due.Error;
        if (now < due.Value) return (WorkItem?)null;
        var unresolved = meetings.Where(item => !item.IsDeleted && item.Status == AppointmentStatus.Scheduled && SchoolTime.Date(item.StartsAt) == day)
            .Select(item => item.Id).ToImmutableHashSet();
        if (unresolved.IsEmpty) return (WorkItem?)null;
        var created = WorkItemRules.FromTemplate(id, template, new WorkItemSource.MissingResults(day, unresolved), due.Value);
        return created.IsFailure ? created.Error : (WorkItem?)created.Value;
    }

    public static Result<WorkItem, ErrorList> RefreshMissingResults(this WorkItem model, IEnumerable<Appointment> appointments)
    {
        if (model.Source is not WorkItemSource.MissingResults source) return model;
        var meetings = appointments.DistinctBy(item => item.Id).ToDictionary(item => item.Id);
        var unresolved = meetings.Values.Where(item => !item.IsDeleted && item.Status == AppointmentStatus.Scheduled && SchoolTime.Date(item.StartsAt) == source.Date)
            .Select(item => item.Id).ToImmutableHashSet();
        if (!model.IsPending)
            return unresolved.IsEmpty ? model : ErrorList.Single(new ModelError.DecisionRequired(), "closedDailyResultsTask");
        if (source.Appointments.Any(id => !meetings.ContainsKey(id)))
            return model with { Source = source with { Appointments = source.Appointments.Union(unresolved) },
                Problems = model.Problems.Add(WorkItemProblem.SourceUnavailable) };

        var included = source.Appointments.Where(id => !meetings[id].IsDeleted && SchoolTime.Date(meetings[id].StartsAt) == source.Date)
            .Union(unresolved).ToImmutableHashSet();
        // If every meeting left the day, retain the last membership only as closed history.
        var updated = included.IsEmpty || included.SetEquals(source.Appointments)
            ? model : model with { Source = source with { Appointments = included } };
        return unresolved.IsEmpty && !updated.IsProblematic
            ? updated with { Status = WorkItemStatus.Completed, PostponedUntil = null }
            : updated;
    }

    public static Result<WorkItem, ErrorList> EnsureVacationFollowUp(WorkItemId id, TaskTemplate template, Vacation vacation,
        IEnumerable<WorkItem> existing)
    {
        if (vacation.Subject is not VacationSubject.Client client) return ErrorList.Single(new ModelError.InvalidValue(), "vacation");
        var matching = existing.Where(item => item.Source is WorkItemSource.VacationFollowUp source && source.VacationId == vacation.Id && source.ClientId == client.Id).ToArray();
        if (matching.Length > 1) return ErrorList.Single(new ModelError.InvalidState(), "existingTasks");
        if (matching.Length == 1) return matching[0].RefreshVacationFollowUp(vacation);
        if (vacation.IsDeleted) return ErrorList.Single(new ModelError.InvalidState(), "vacation");
        var created = WorkItemRules.FromTemplate(id, template, new WorkItemSource.VacationFollowUp(client.Id, vacation.Id), vacation.Period.EndsAt);
        return created.IsFailure ? created.Error : AppendVacationComment(created.Value, vacation);
    }

    public static WorkItem RefreshVacationFollowUp(this WorkItem model, Vacation vacation)
    {
        if (!model.IsPending || model.Source is not WorkItemSource.VacationFollowUp source || source.VacationId != vacation.Id ||
            vacation.Subject is not VacationSubject.Client client || source.ClientId != client.Id) return model;
        if (vacation.IsDeleted) return model.CancelAutomatically("vacation.deleted");
        // Keep the task text from its template; the current comment is a separate subject field.
        return AppendVacationComment(model with { DueAt = vacation.Period.EndsAt }, vacation);
    }

    private static WorkItem AppendVacationComment(WorkItem model, Vacation vacation) => model with { SubjectComment = vacation.Comment };
}
