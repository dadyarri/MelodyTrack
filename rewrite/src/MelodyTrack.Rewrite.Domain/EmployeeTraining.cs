using System.Collections.Immutable;

namespace MelodyTrack.Rewrite.Domain;

public enum EmployeeTrainingStatus { NotStarted, InProgress, Completed }
public enum EmployeeTrainingAutoStart { None, FirstSuccessfulSignIn, WorkdayStarted, WorkFormClosed }

/// <summary>Expected values belong to an isolated fictional example, never to an application command.</summary>
public sealed record EmployeeTrainingStep(string Id, ImmutableDictionary<string, string> ExpectedState);

public sealed record EmployeeTrainingScenario
{
    internal EmployeeTrainingScenario(string id, int version, ImmutableDictionary<string, string> initialState,
        ImmutableArray<EmployeeTrainingStep> steps)
    { Id = id; Version = version; InitialState = initialState; Steps = steps; }

    public string Id { get; }
    public int Version { get; }
    public ImmutableDictionary<string, string> InitialState { get; }
    public ImmutableArray<EmployeeTrainingStep> Steps { get; }
}

public sealed record EmployeeTrainingProgress
{
    internal EmployeeTrainingProgress(StaffId staffId, EmployeeTrainingScenario scenario)
        : this(staffId, scenario.Id, scenario.Version, scenario.InitialState) { }

    internal EmployeeTrainingProgress(StaffId staffId, string scenarioId, int version, ImmutableDictionary<string, string> demoState)
    { StaffId = staffId; ScenarioId = scenarioId; Version = version; DemoState = demoState; }

    public StaffId StaffId { get; }
    public string ScenarioId { get; }
    public int Version { get; internal init; }
    public int? CompletedVersion { get; internal init; }
    public EmployeeTrainingStatus Status { get; internal init; }
    public int NextStep { get; internal init; }
    public ImmutableDictionary<string, string> DemoState { get; internal init; }
    public bool IsRunning { get; internal init; }
    public bool WasDeferred { get; internal init; }
    public bool IsDeferred => WasDeferred && !IsRunning && Status == EmployeeTrainingStatus.InProgress;
    public bool AutoStartSkippedForOpenForm { get; internal init; }
    public bool IsRequired => CompletedVersion != Version;
    public bool BlocksOrdinaryWork => IsRunning && Status == EmployeeTrainingStatus.InProgress && IsRequired;
    public bool HasRequiredUpdate => CompletedVersion is { } completed && completed < Version;
}

public sealed record EmployeeTrainingArticleProgress
{
    internal EmployeeTrainingArticleProgress(StaffId staffId, string articleId, int version)
    { StaffId = staffId; ArticleId = articleId; Version = version; }

    public StaffId StaffId { get; }
    public string ArticleId { get; }
    public int Version { get; internal init; }
    public int? ReadVersion { get; internal init; }
    public bool HasNewUpdate { get; internal init; }
}
