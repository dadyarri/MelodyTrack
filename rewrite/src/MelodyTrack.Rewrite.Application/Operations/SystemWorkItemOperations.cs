using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

/// <summary>Pure coordination results must be persisted with their source change in one transaction.</summary>
public static class SystemWorkItemOperations
{
    public static Result<WorkItem?, ErrorList> EnsureLessonReminder(WorkItemId id, TaskTemplate template, Appointment appointment,
        Service service, TimeSpan lead, IEnumerable<WorkItem> existing, DateTimeOffset now) =>
        SystemWorkItemRules.EnsureLessonReminder(id, template, appointment, service, lead, existing, now);

    public static Result<WorkItem?, ErrorList> EnsureBirthday(WorkItemId id, TaskTemplate template, Client client, DateOnly day,
        IEnumerable<WorkItem> existing, DateTimeOffset now, TimeOnly morningTime) => SystemWorkItemRules.EnsureBirthday(id, template, client, day, existing, now, morningTime);

    public static Result<WorkItem?, ErrorList> EnsureTeacherSchedule(WorkItemId id, TaskTemplate template, Staff staff, DateOnly day,
        IEnumerable<Appointment> appointments, IEnumerable<WorkItem> existing, DateTimeOffset now, TimeOnly morningTime) =>
        SystemWorkItemRules.EnsureTeacherSchedule(id, template, staff, day, appointments, existing, now, morningTime);

    public static Result<WorkItem?, ErrorList> EnsureMissingResults(WorkItemId id, TaskTemplate template, DateOnly day,
        IEnumerable<Appointment> appointments, IEnumerable<WorkItem> existing, DateTimeOffset now, TimeOnly morningTime) =>
        SystemWorkItemRules.EnsureMissingResults(id, template, day, appointments, existing, now, morningTime);

    public static Result<WorkItem, ErrorList> EnsureVacationFollowUp(WorkItemId id, TaskTemplate template, Vacation vacation,
        IEnumerable<WorkItem> existing) => SystemWorkItemRules.EnsureVacationFollowUp(id, template, vacation, existing);
}
