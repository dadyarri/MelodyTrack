using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public enum WorkItemStatus { Open, Postponed, Completed, Cancelled }
public enum TaskTemplateScope { NewOnly, OpenAndPostponed }
public enum ReminderKind { Consultation, Homework, Appointment }
public enum SystemTaskKind { ConsultationReminder, HomeworkReminder, DebtReminder, AbsenceNotification, RescheduleNotification, SeriesConflict }
public sealed record AppointmentMove(DateTimeOffset OriginalStart, DateTimeOffset CurrentStart);

public closed record WorkItemSource
{
    public sealed record OneOff : WorkItemSource;
    public sealed record Calendar(TaskRuleId RuleId, DateTimeOffset ScheduledFor) : WorkItemSource;
    public sealed record LessonReminder(AppointmentId AppointmentId, ReminderKind Kind) : WorkItemSource;
    public sealed record Debt(ClientId ClientId) : WorkItemSource;
    public sealed record Absence(ClientId ClientId, AbsenceCause Cause) : WorkItemSource;
    public sealed record Reschedule(ClientId ClientId, ImmutableDictionary<AppointmentId, AppointmentMove> Moves) : WorkItemSource;
    public sealed record SeriesConflict(SeriesId SeriesId, ImmutableHashSet<SequenceNumber> Occurrences) : WorkItemSource;

    internal UnitResult<ErrorList> Validate() => this switch
    {
        OneOff => UnitResult.Success<ErrorList>(),
        Calendar calendar => Validation.Identifier(calendar.RuleId.Value, "ruleId"),
        LessonReminder reminder => Validation.Collect(Validation.Identifier(reminder.AppointmentId.Value, "appointmentId"),
            Enum.IsDefined(reminder.Kind) ? UnitResult.Success<ErrorList>() : UnitResult.Failure(ErrorList.Single(new ModelError.InvalidValue(), "kind"))),
        Debt debt => Validation.Identifier(debt.ClientId.Value, "clientId"),
        Absence absence => Validation.Collect(Validation.Identifier(absence.ClientId.Value, "clientId"), absence.Cause switch
        {
            AbsenceCause.Vacation vacation => Validation.Identifier(vacation.Id.Value, "vacationId"),
            AbsenceCause.SchoolClosure closure => Validation.Identifier(closure.Id.Value, "closureId")
        }),
        Reschedule reschedule => Validation.Collect(Validation.Identifier(reschedule.ClientId.Value, "clientId"),
            Validation.Collect(reschedule.Moves.Keys.Select(key => Validation.Identifier(key.Value, "appointmentId")).ToArray())),
        SeriesConflict conflict => Validation.Identifier(conflict.SeriesId.Value, "seriesId")
    };
}

public sealed record TaskTemplate
{
    internal TaskTemplate(TaskTemplateId id, string title, string content, string? message)
    { Id = id; Title = title; Content = content; Message = message; }

    public TaskTemplateId Id { get; }

    public string Title { get; }

    public string Content { get; }

    public string? Message { get; }

    public bool IsEnabled { get; internal init; } = true;
}

public sealed record WorkItem
{
    internal WorkItem(WorkItemId id, string title, string content, string? message, DateTimeOffset dueAt,
        WorkItemSource source, TaskTemplateId? templateId)
    { Id = id; Title = title; Content = content; Message = message; DueAt = dueAt.ToUniversalTime(); Source = source; TemplateId = templateId; }

    public WorkItemId Id { get; }

    public string Title { get; internal init; }

    public string Content { get; internal init; }

    public string? Message { get; internal init; }

    public DateTimeOffset DueAt { get; internal init; }

    public DateTimeOffset? PostponedUntil { get; internal init; }

    public DateTimeOffset EffectiveDueAt => PostponedUntil ?? DueAt;

    public WorkItemSource Source { get; internal init; }

    public TaskTemplateId? TemplateId { get; }

    public WorkItemStatus Status { get; internal init; } = WorkItemStatus.Open;

    public bool IsPending => Status is WorkItemStatus.Open or WorkItemStatus.Postponed;

    public string? CancellationReason { get; internal init; }

    public ImmutableHashSet<string> IndividualOverrides { get; internal init; } = [];
}

public closed record CalendarTaskPattern
{
    public sealed record Daily : CalendarTaskPattern;
    public sealed record Weekdays : CalendarTaskPattern
    {
        private Weekdays(ImmutableHashSet<DayOfWeek> days) => Days = days;
        public ImmutableHashSet<DayOfWeek> Days { get; }
        public static Result<Weekdays, ErrorList> Create(IEnumerable<DayOfWeek> days)
        {
            var values = days.ToImmutableHashSet();
            return values.IsEmpty || values.Any(day => !Enum.IsDefined(day))
                ? ErrorList.Single(new ModelError.InvalidValue(), "weekdays") : new Weekdays(values);
        }
    }
    public sealed record Monthly : CalendarTaskPattern
    {
        private Monthly(int day) => Day = day;
        public int Day { get; }
        public static Result<Monthly, ErrorList> Create(int day) => day is < 1 or > 31
            ? ErrorList.Single(new ModelError.InvalidValue(), "day") : new Monthly(day);
    }
    public sealed record Every : CalendarTaskPattern
    {
        private Every(int days) => Days = days;
        public int Days { get; }
        public static Result<Every, ErrorList> DaysApart(int days) => days <= 0
            ? ErrorList.Single(new ModelError.InvalidValue(), "interval") : new Every(days);
        public static Result<Every, ErrorList> WeeksApart(int weeks) => weeks <= 0 || weeks > int.MaxValue / 7
            ? ErrorList.Single(new ModelError.InvalidValue(), "interval") : new Every(weeks * 7);
    }
}

public sealed record CalendarTaskRule
{
    internal CalendarTaskRule(TaskRuleId id, TaskTemplateId templateId, DateOnly start, DateOnly? end, TimeOnly time, CalendarTaskPattern pattern)
    { Id = id; TemplateId = templateId; Start = start; End = end; Time = time; Pattern = pattern; }

    public TaskRuleId Id { get; }

    public TaskTemplateId TemplateId { get; }

    public DateOnly Start { get; }

    public DateOnly? End { get; }

    public TimeOnly Time { get; }

    public CalendarTaskPattern Pattern { get; }

    public bool IsEnabled { get; internal init; } = true;
}

public sealed record SystemTaskRule
{
    internal SystemTaskRule(TaskRuleId id, TaskTemplateId templateId, SystemTaskKind kind, TimeSpan lead)
    { Id = id; TemplateId = templateId; Kind = kind; Lead = lead; }

    public TaskRuleId Id { get; }

    public TaskTemplateId TemplateId { get; }

    public SystemTaskKind Kind { get; }

    public TimeSpan Lead { get; }

    public bool IsEnabled { get; internal init; } = true;

    public static TimeSpan DefaultHomeworkLead => TimeSpan.FromHours(72);
}
