using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class TaskTemplateRules
{
    public static Result<TaskTemplate, ErrorList> Create(TaskTemplateId id, string? title, string? content, string? message)
    {
        var validTitle = Validation.Required("title", title);
        var validContent = Validation.Required("content", content);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validTitle), Validation.Check(validContent));
        return validation.IsFailure ? validation.Error : new TaskTemplate(id, validTitle.Value, validContent.Value, Validation.OptionalText(message));
    }

    public static Result<TaskTemplate, ErrorList> Revise(this TaskTemplate model, string? title, string? content, string? message)
    {
        var template = Create(model.Id, title, content, message);
        return template.IsFailure ? template.Error : template.Value with { IsEnabled = model.IsEnabled };
    }

    public static Result<TaskTemplate, ErrorList> SetEnabled(this TaskTemplate model, bool enabled) => model with { IsEnabled = enabled };
}

public static class WorkItemRules
{
    public static Result<WorkItem, ErrorList> CreateOneOff(WorkItemId id, string? title, string? content, DateTimeOffset dueAt)
    {
        var validTitle = Validation.Required("title", title);
        var validContent = Validation.Required("content", content);
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Check(validTitle), Validation.Check(validContent));
        return validation.IsFailure ? validation.Error : new WorkItem(id, validTitle.Value, validContent.Value, null, dueAt, new WorkItemSource.OneOff(), null);
    }

    public static Result<WorkItem, ErrorList> FromTemplate(WorkItemId id, TaskTemplate template, WorkItemSource source, DateTimeOffset dueAt)
    {
        if (!template.IsEnabled) return ErrorList.Single(new ModelError.InvalidState());
        var validation = Validation.Collect(Validation.Identifier(id.Value), source.Validate());
        return validation.IsFailure ? validation.Error : new WorkItem(id, template.Title, template.Content, template.Message, dueAt, source, template.Id);
    }

    private static Result<WorkItem, ErrorList> Close(this WorkItem model, WorkItemStatus status)
    {
        if (!model.IsPending) return ErrorList.Single(new ModelError.InvalidState());
        return model with { Status = status, PostponedUntil = null };
    }

    public static Result<WorkItem, ErrorList> Complete(this WorkItem model) => model.Close(WorkItemStatus.Completed);

    public static Result<WorkItem, ErrorList> Cancel(this WorkItem model) => model.Close(WorkItemStatus.Cancelled);

    public static Result<WorkItem, ErrorList> Postpone(this WorkItem model, DateTimeOffset until, DateTimeOffset now)
    {
        if (!model.IsPending) return ErrorList.Single(new ModelError.InvalidState());
        if (until <= now) return ErrorList.Single(new ModelError.InvalidValue(), "until");
        return model with { Status = WorkItemStatus.Postponed, PostponedUntil = until.ToUniversalTime() };
    }

    public static Result<WorkItem, ErrorList> Reopen(this WorkItem model, IEnumerable<WorkItem> others)
    {
        if (model.IsPending) return ErrorList.Single(new ModelError.InvalidState());
        if (model.Source is WorkItemSource.Calendar calendar && others.Any(item => item.Id != model.Id && item.IsPending &&
            item.Source is WorkItemSource.Calendar other && other.RuleId == calendar.RuleId))
            return ErrorList.Single(new ModelError.DecisionRequired(), "existingOpenTask");
        return model with { Status = WorkItemStatus.Open, CancellationReason = null, PostponedUntil = null };
    }

    public static WorkItem RefreshTemplate(this WorkItem model, TaskTemplate template, TaskTemplateScope scope) => !model.IsPending || model.TemplateId != template.Id || scope == TaskTemplateScope.NewOnly
        ? model : model with
        {
            Title = model.IndividualOverrides.Contains("title") ? model.Title : template.Title,
            Content = model.IndividualOverrides.Contains("content") ? model.Content : template.Content,
            Message = model.IndividualOverrides.Contains("message") ? model.Message : template.Message
        };

    public static WorkItem CancelAutomatically(this WorkItem model, string reason) => model.IsPending
        ? model with { Status = WorkItemStatus.Cancelled, CancellationReason = reason, PostponedUntil = null } : model;

    public static Result<WorkItem, ErrorList> RecalculateReminder(this WorkItem model, Appointment appointment, TimeSpan lead, DateTimeOffset now)
    {
        if (!model.IsPending || model.Source is not WorkItemSource.LessonReminder reminder || reminder.AppointmentId != appointment.Id) return model;
        if (appointment.IsDeleted || appointment.Status == AppointmentStatus.Cancelled) return model.CancelAutomatically("appointment.cancelled");
        if (lead < TimeSpan.Zero) return ErrorList.Single(new ModelError.InvalidValue(), "lead");
        try
        {
            var due = appointment.StartsAt - lead;
            return model with { DueAt = due < now ? now.ToUniversalTime() : due };
        }
        catch (ArgumentOutOfRangeException) { return ErrorList.Single(new TimeError.OutOfRange()); }
    }

    public static WorkItem RefreshDebt(this WorkItem model, decimal balance) => model.Source is WorkItemSource.Debt && balance >= 0
        ? model.CancelAutomatically("debt.repaid") : model;

    public static Result<WorkItem, ErrorList> AddMoves(this WorkItem model, ClientId clientId, IEnumerable<KeyValuePair<AppointmentId, AppointmentMove>> moves)
    {
        if (!model.IsPending || model.Source is not WorkItemSource.Reschedule source || source.ClientId != clientId)
            return ErrorList.Single(new ModelError.InvalidState());
        var values = source.Moves;
        foreach (var pair in moves)
        {
            var move = values.TryGetValue(pair.Key, out var previous)
                ? new AppointmentMove(previous.OriginalStart, pair.Value.CurrentStart.ToUniversalTime())
                : new AppointmentMove(pair.Value.OriginalStart.ToUniversalTime(), pair.Value.CurrentStart.ToUniversalTime());
            values = values.SetItem(pair.Key, move);
        }
        return model with { Source = source with { Moves = values } };
    }

    public static Result<WorkItem, ErrorList> AddConflicts(this WorkItem model, SeriesId seriesId, IEnumerable<SequenceNumber> occurrences) =>
        model.IsPending && model.Source is WorkItemSource.SeriesConflict source && source.SeriesId == seriesId
            ? model with { Source = source with { Occurrences = source.Occurrences.Union(occurrences) } }
            : ErrorList.Single(new ModelError.InvalidState());
}

public static class CalendarTaskRules
{
    public static Result<CalendarTaskRule, ErrorList> Create(TaskRuleId id, TaskTemplateId templateId, DateOnly start, DateOnly? end, TimeOnly time, CalendarTaskPattern pattern)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(templateId.Value, "templateId"));
        if (validation.IsFailure) return validation.Error;
        return end < start ? ErrorList.Single(new ModelError.InvalidValue(), "end") : new CalendarTaskRule(id, templateId, start, end, time, pattern);
    }

    public static Result<CalendarTaskRule, ErrorList> SetEnabled(this CalendarTaskRule model, bool enabled) => model with { IsEnabled = enabled };

    public static Result<DateTimeOffset?, ErrorList> NextAfter(this CalendarTaskRule model, DateTimeOffset after, IEnumerable<WorkItem> items)
    {
        var history = items.Where(item => item.Source is WorkItemSource.Calendar calendar && calendar.RuleId == model.Id).ToArray();
        if (!model.IsEnabled || history.Any(item => item.IsPending)) return (DateTimeOffset?)null;
        var date = SchoolTime.Date(after);
        if (date < model.Start) date = model.Start;
        try
        {
            if (model.Pattern is CalendarTaskPattern.Every every)
            {
                var difference = date.DayNumber - model.Start.DayNumber;
                var remainder = difference % every.Days;
                if (remainder != 0) date = date.AddDays(every.Days - remainder);
            }
            while (true)
            {
                if (model.End is { } end && date > end) return (DateTimeOffset?)null;
                var matches = model.Pattern switch
                {
                    CalendarTaskPattern.Daily => true,
                    CalendarTaskPattern.Weekdays weekdays => weekdays.Days.Contains(date.DayOfWeek),
                    CalendarTaskPattern.Monthly monthly => date.Day == Math.Min(monthly.Day, DateTime.DaysInMonth(date.Year, date.Month)),
                    CalendarTaskPattern.Every => true
                };
                if (matches)
                {
                    var instant = SchoolTime.At(date, model.Time);
                    if (instant.IsFailure) return instant.Error;
                    if (instant.Value > after && !history.Any(item => item.Source is WorkItemSource.Calendar calendar && calendar.ScheduledFor == instant.Value))
                        return (DateTimeOffset?)instant.Value;
                }
                if (date == DateOnly.MaxValue) return (DateTimeOffset?)null;
                date = date.AddDays(model.Pattern is CalendarTaskPattern.Every interval ? interval.Days : 1);
            }
        }
        catch (ArgumentOutOfRangeException) { return (DateTimeOffset?)null; }
    }
}

public static class SystemTaskRules
{
    public static Result<SystemTaskRule, ErrorList> Create(TaskRuleId id, TaskTemplateId templateId, SystemTaskKind kind, TimeSpan lead)
    {
        var validation = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(templateId.Value, "templateId"));
        if (validation.IsFailure) return validation.Error;
        if (!Enum.IsDefined(kind) || lead < TimeSpan.Zero) return ErrorList.Single(new ModelError.InvalidValue(), "rule");
        return new SystemTaskRule(id, templateId, kind, lead);
    }

    public static Result<SystemTaskRule, ErrorList> SetEnabled(this SystemTaskRule model, bool enabled) => model with { IsEnabled = enabled };
}
