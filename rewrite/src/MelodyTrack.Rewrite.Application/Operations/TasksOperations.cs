using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

public static class TaskTemplateOperations
{
    public static Result<TaskTemplate, ErrorList> Create(TaskTemplateId id, string? title, string? content, string? message, Actor actor)
    {
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        return TaskTemplateRules.Create(id, title, content, message);
    }

    public static Result<TaskTemplate, ErrorList> Revise(this TaskTemplate model, string? title, string? content, string? message, Actor actor)
    {
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        return TaskTemplateRules.Revise(model, title, content, message);
    }

    public static Result<TaskTemplate, ErrorList> SetEnabled(this TaskTemplate model, bool enabled, Actor actor)
    {
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        return TaskTemplateRules.SetEnabled(model, enabled);
    }
}

public static class WorkItemOperations
{
    public static Result<WorkItem, ErrorList> CreateOneOff(WorkItemId id, string? title, string? content, DateTimeOffset dueAt, Actor actor)
    {
        if (!actor.IsReception()) return ErrorList.Single(new ModelError.Forbidden());
        return WorkItemRules.CreateOneOff(id, title, content, dueAt);
    }

    public static Result<WorkItem, ErrorList> Complete(this WorkItem model, Actor actor)
    {
        if (!actor.IsReception()) return ErrorList.Single(new ModelError.Forbidden());
        return WorkItemRules.Complete(model);
    }

    public static Result<WorkItem, ErrorList> Cancel(this WorkItem model, Actor actor)
    {
        if (!actor.IsReception()) return ErrorList.Single(new ModelError.Forbidden());
        return WorkItemRules.Cancel(model);
    }

    public static Result<WorkItem, ErrorList> Postpone(this WorkItem model, DateTimeOffset until, DateTimeOffset now, Actor actor)
    {
        if (!actor.IsReception()) return ErrorList.Single(new ModelError.Forbidden());
        return WorkItemRules.Postpone(model, until, now);
    }

    public static Result<WorkItem, ErrorList> Reopen(this WorkItem model, Actor actor, IEnumerable<WorkItem> others, WorkItemReopenContext current)
    {
        if (!actor.IsReception()) return ErrorList.Single(new ModelError.Forbidden());
        return WorkItemRules.Reopen(model, others, current);
    }
}

public static class CalendarTaskRuleOperations
{
    public static Result<CalendarTaskRule, ErrorList> Create(TaskRuleId id, TaskTemplateId templateId, DateOnly start, DateOnly? end, TimeOnly time, CalendarTaskPattern pattern, Actor actor)
    {
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        return CalendarTaskRules.Create(id, templateId, start, end, time, pattern);
    }

    public static Result<CalendarTaskRule, ErrorList> SetEnabled(this CalendarTaskRule model, bool enabled, Actor actor)
    {
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        return CalendarTaskRules.SetEnabled(model, enabled);
    }
}

public static class SystemTaskRuleOperations
{
    public static Result<SystemTaskRule, ErrorList> Create(TaskRuleId id, TaskTemplateId templateId, SystemTaskKind kind, TimeSpan lead, Actor actor)
    {
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        return SystemTaskRules.Create(id, templateId, kind, lead);
    }

    public static Result<SystemTaskRule, ErrorList> SetEnabled(this SystemTaskRule model, bool enabled, Actor actor)
    {
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        return SystemTaskRules.SetEnabled(model, enabled);
    }
}
