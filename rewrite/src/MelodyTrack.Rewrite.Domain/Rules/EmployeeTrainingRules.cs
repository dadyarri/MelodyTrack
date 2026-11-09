using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class EmployeeTrainingRules
{
    public static Result<EmployeeTrainingScenario, ErrorList> CreateScenario(string? id, int version,
        ImmutableDictionary<string, string> initialState, IEnumerable<EmployeeTrainingStep> steps)
    {
        var name = Validation.Required("scenarioId", id);
        if (name.IsFailure) return name.Error;
        var values = steps.ToImmutableArray();
        if (version <= 0) return ErrorList.Single(new ModelError.InvalidValue(), "version");
        if (values.IsEmpty || values.Any(step => string.IsNullOrWhiteSpace(step.Id) || step.ExpectedState.IsEmpty ||
                step.ExpectedState.Keys.Any(string.IsNullOrWhiteSpace)) || values.Select(step => step.Id).Distinct().Count() != values.Length)
            return ErrorList.Single(new ModelError.InvalidValue(), "steps");
        if (initialState.Keys.Any(string.IsNullOrWhiteSpace)) return ErrorList.Single(new ModelError.InvalidValue(), "initialState");
        return new EmployeeTrainingScenario(name.Value, version, initialState, values);
    }

    public static Result<EmployeeTrainingProgress, ErrorList> CreateProgress(StaffId staffId, EmployeeTrainingScenario scenario)
    {
        var validation = Validation.Identifier(staffId.Value, "staffId");
        return validation.IsFailure ? validation.Error : new EmployeeTrainingProgress(staffId, scenario);
    }

    public static Result<EmployeeTrainingProgress, ErrorList> RestoreProgress(StaffId staffId, string? scenarioId, int version,
        int? completedVersion, EmployeeTrainingStatus status, int nextStep, ImmutableDictionary<string, string> demoState,
        bool isRunning, bool wasDeferred, bool autoStartSkippedForOpenForm)
    {
        var name = Validation.Required("scenarioId", scenarioId);
        var validation = Validation.Collect(Validation.Identifier(staffId.Value, "staffId"), Validation.Check(name));
        if (validation.IsFailure) return validation.Error;
        if (version <= 0 || completedVersion is <= 0 || completedVersion > version || !Enum.IsDefined(status) || nextStep < 0 ||
            demoState.Keys.Any(string.IsNullOrWhiteSpace)) return ErrorList.Single(new ModelError.InvalidValue(), "progress");
        if (status == EmployeeTrainingStatus.NotStarted && (nextStep != 0 || isRunning || wasDeferred) ||
            status == EmployeeTrainingStatus.Completed && (completedVersion != version || nextStep == 0 || isRunning) ||
            isRunning && status != EmployeeTrainingStatus.InProgress)
            return ErrorList.Single(new ModelError.InvalidState(), "progress");
        return new EmployeeTrainingProgress(staffId, name.Value, version, demoState) { CompletedVersion = completedVersion, Status = status,
            NextStep = nextStep, IsRunning = isRunning, WasDeferred = wasDeferred, AutoStartSkippedForOpenForm = autoStartSkippedForOpenForm };
    }

    public static Result<EmployeeTrainingProgress, ErrorList> AutoStart(this EmployeeTrainingProgress model,
        EmployeeTrainingAutoStart trigger, bool hasOpenWorkForm)
    {
        if (!Enum.IsDefined(trigger)) return ErrorList.Single(new ModelError.InvalidValue(), "trigger");
        if (model.WasDeferred || !model.IsRequired || model.Status == EmployeeTrainingStatus.Completed || model.IsRunning || trigger == EmployeeTrainingAutoStart.None) return model;
        if (trigger == EmployeeTrainingAutoStart.WorkFormClosed && !model.AutoStartSkippedForOpenForm) return model;
        if (trigger == EmployeeTrainingAutoStart.WorkdayStarted && !model.HasRequiredUpdate && !model.AutoStartSkippedForOpenForm) return model;
        if (hasOpenWorkForm) return model with { AutoStartSkippedForOpenForm = true };
        return model with { Status = EmployeeTrainingStatus.InProgress, IsRunning = true, AutoStartSkippedForOpenForm = false };
    }

    public static Result<EmployeeTrainingProgress, ErrorList> StartManually(this EmployeeTrainingProgress model)
    {
        if (model.Status == EmployeeTrainingStatus.Completed) return ErrorList.Single(new ModelError.InvalidState());
        return model with { Status = EmployeeTrainingStatus.InProgress, IsRunning = true, AutoStartSkippedForOpenForm = false };
    }

    public static Result<EmployeeTrainingProgress, ErrorList> Defer(this EmployeeTrainingProgress model)
    {
        if (!model.IsRunning || model.Status != EmployeeTrainingStatus.InProgress) return ErrorList.Single(new ModelError.InvalidState());
        return model with { IsRunning = false, WasDeferred = true };
    }

    public static Result<EmployeeTrainingProgress, ErrorList> ApplyDemoValue(this EmployeeTrainingProgress model, string? key, string value)
    {
        if (!model.IsRunning || model.Status != EmployeeTrainingStatus.InProgress) return ErrorList.Single(new ModelError.InvalidState());
        var name = Validation.Required("key", key);
        return name.IsFailure ? name.Error : model with { DemoState = model.DemoState.SetItem(name.Value, value) };
    }

    public static Result<EmployeeTrainingProgress, ErrorList> CompleteStep(this EmployeeTrainingProgress model, EmployeeTrainingScenario scenario)
    {
        if (!model.IsRunning || model.Status != EmployeeTrainingStatus.InProgress || scenario.Id != model.ScenarioId ||
            scenario.Version != model.Version || model.NextStep >= scenario.Steps.Length)
            return ErrorList.Single(new ModelError.InvalidState());
        var expected = scenario.Steps[model.NextStep].ExpectedState;
        if (!expected.All(pair => model.DemoState.TryGetValue(pair.Key, out var value) && value == pair.Value))
            return ErrorList.Single(new ModelError.InvalidState(), "expectedDemoState");
        var next = model.NextStep + 1;
        return next == scenario.Steps.Length
            ? model with { NextStep = next, Status = EmployeeTrainingStatus.Completed, CompletedVersion = model.Version, IsRunning = false }
            : model with { NextStep = next };
    }

    public static Result<EmployeeTrainingProgress, ErrorList> Restart(this EmployeeTrainingProgress model, EmployeeTrainingScenario scenario)
    {
        if (scenario.Id != model.ScenarioId || scenario.Version != model.Version) return ErrorList.Single(new ModelError.InvalidValue(), "scenario");
        return model with { Status = EmployeeTrainingStatus.InProgress, NextStep = 0, DemoState = scenario.InitialState,
            IsRunning = true, AutoStartSkippedForOpenForm = false };
    }

    public static Result<EmployeeTrainingProgress, ErrorList> ApplyVersion(this EmployeeTrainingProgress model, EmployeeTrainingScenario scenario)
    {
        if (scenario.Id != model.ScenarioId || scenario.Version < model.Version) return ErrorList.Single(new ModelError.InvalidValue(), "version");
        if (scenario.Version == model.Version) return model;
        var unfinished = model.Status == EmployeeTrainingStatus.InProgress;
        return model with { Version = scenario.Version,
            Status = unfinished ? EmployeeTrainingStatus.InProgress : EmployeeTrainingStatus.NotStarted,
            NextStep = 0, DemoState = scenario.InitialState, IsRunning = unfinished && model.IsRunning,
            WasDeferred = unfinished && model.WasDeferred,
            AutoStartSkippedForOpenForm = model.Status != EmployeeTrainingStatus.Completed && model.AutoStartSkippedForOpenForm };
    }
}

public static class EmployeeTrainingArticleRules
{
    public static Result<EmployeeTrainingArticleProgress, ErrorList> Create(StaffId staffId, string? articleId, int version)
    {
        var name = Validation.Required("articleId", articleId);
        var validation = Validation.Collect(Validation.Identifier(staffId.Value, "staffId"), Validation.Check(name));
        if (validation.IsFailure) return validation.Error;
        return version <= 0 ? ErrorList.Single(new ModelError.InvalidValue(), "version") : new EmployeeTrainingArticleProgress(staffId, name.Value, version);
    }

    public static Result<EmployeeTrainingArticleProgress, ErrorList> ApplyVersion(this EmployeeTrainingArticleProgress model, int version)
    {
        if (version < model.Version) return ErrorList.Single(new ModelError.InvalidValue(), "version");
        return version == model.Version ? model : model with { Version = version, HasNewUpdate = true };
    }

    public static EmployeeTrainingArticleProgress OpenForReading(this EmployeeTrainingArticleProgress model) =>
        model with { ReadVersion = model.Version, HasNewUpdate = false };

    public static Result<EmployeeTrainingArticleProgress, ErrorList> Restore(StaffId staffId, string? articleId, int version,
        int? readVersion, bool hasNewUpdate)
    {
        var created = Create(staffId, articleId, version);
        if (created.IsFailure) return created.Error;
        if (readVersion is <= 0 || readVersion > version || readVersion == version && hasNewUpdate)
            return ErrorList.Single(new ModelError.InvalidState(), "articleProgress");
        return created.Value with { ReadVersion = readVersion, HasNewUpdate = hasNewUpdate };
    }
}
