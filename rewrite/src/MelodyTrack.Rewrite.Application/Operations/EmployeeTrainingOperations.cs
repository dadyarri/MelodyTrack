using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

/// <summary>Available scenario IDs come from current authorization; training never grants an operation permission.</summary>
public static class EmployeeTrainingOperations
{
    private static bool CanUse(EmployeeTrainingProgress model, Actor actor, ImmutableHashSet<string> availableScenarios) =>
        actor is Actor.Staff staff && staff.Id == model.StaffId && availableScenarios.Contains(model.ScenarioId);

    public static Result<EmployeeTrainingProgress, ErrorList> StartManually(this EmployeeTrainingProgress model, Actor actor,
        ImmutableHashSet<string> availableScenarios) => !CanUse(model, actor, availableScenarios)
        ? ErrorList.Single(new ModelError.Forbidden()) : EmployeeTrainingRules.StartManually(model);

    public static Result<EmployeeTrainingProgress, ErrorList> AutoStart(this EmployeeTrainingProgress model, Actor actor,
        ImmutableHashSet<string> availableScenarios, EmployeeTrainingAutoStart trigger, bool hasOpenWorkForm) =>
        !CanUse(model, actor, availableScenarios) ? ErrorList.Single(new ModelError.Forbidden()) : EmployeeTrainingRules.AutoStart(model, trigger, hasOpenWorkForm);

    public static Result<EmployeeTrainingProgress, ErrorList> ApplyDemoValue(this EmployeeTrainingProgress model, Actor actor,
        ImmutableHashSet<string> availableScenarios, string? key, string value) => !CanUse(model, actor, availableScenarios)
        ? ErrorList.Single(new ModelError.Forbidden()) : EmployeeTrainingRules.ApplyDemoValue(model, key, value);

    public static Result<EmployeeTrainingProgress, ErrorList> CompleteStep(this EmployeeTrainingProgress model, Actor actor,
        ImmutableHashSet<string> availableScenarios, EmployeeTrainingScenario scenario) => !CanUse(model, actor, availableScenarios)
        ? ErrorList.Single(new ModelError.Forbidden()) : EmployeeTrainingRules.CompleteStep(model, scenario);

    public static Result<EmployeeTrainingProgress, ErrorList> Defer(this EmployeeTrainingProgress model, Actor actor,
        ImmutableHashSet<string> availableScenarios) => !CanUse(model, actor, availableScenarios)
        ? ErrorList.Single(new ModelError.Forbidden()) : EmployeeTrainingRules.Defer(model);

    public static Result<EmployeeTrainingProgress, ErrorList> Restart(this EmployeeTrainingProgress model, Actor actor,
        ImmutableHashSet<string> availableScenarios, EmployeeTrainingScenario scenario) => !CanUse(model, actor, availableScenarios)
        ? ErrorList.Single(new ModelError.Forbidden()) : EmployeeTrainingRules.Restart(model, scenario);

    public static Result<EmployeeTrainingArticleProgress, ErrorList> OpenForReading(this EmployeeTrainingArticleProgress model,
        Actor actor, ImmutableHashSet<string> availableArticles) => actor is Actor.Staff staff && staff.Id == model.StaffId && availableArticles.Contains(model.ArticleId)
        ? EmployeeTrainingArticleRules.OpenForReading(model) : ErrorList.Single(new ModelError.Forbidden());
}
