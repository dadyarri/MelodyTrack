using System.Collections.Immutable;
using System.Text.Json;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Persistence.Training;

/// <summary>Stores personal training fields explicitly; only fictional demonstration values use versioned JSON.</summary>
public sealed class EmployeeTrainingStore(Database database) : IEmployeeTrainingStore
{
    private const string ProgressColumns = """
        staff_id AS "StaffId", scenario_id AS "ScenarioId", version AS "Version", completed_version AS "CompletedVersion",
        status::text AS "Status", next_step AS "NextStep", demo_state_schema_version AS "DemoStateSchemaVersion",
        demo_state::text AS "DemoState", is_running AS "IsRunning", was_deferred AS "WasDeferred",
        auto_start_skipped_for_open_form AS "AutoStartSkippedForOpenForm", revision AS "Revision"
        """;
    private const string ArticleColumns = """
        staff_id AS "StaffId", article_id AS "ArticleId", version AS "Version", read_version AS "ReadVersion",
        has_new_update AS "HasNewUpdate", revision AS "Revision"
        """;

    /// <summary>Reads one employee's scenario progress without loading any other account.</summary>
    public async Task<Result<StoredEmployeeTrainingProgress?, ErrorList>> ReadProgressAsync(StaffId staffId, string scenarioId, CancellationToken cancellationToken)
    {
        var row = await database.Query($"SELECT {ProgressColumns} FROM public.employee_training_progress WHERE staff_id = @staffId AND scenario_id = @scenarioId")
            .WithParameter("staffId", staffId.Value).WithParameter("scenarioId", scenarioId).GetSingleOrDefaultAsync<ProgressRow>(cancellationToken);
        if (row is null) return Result.Success<StoredEmployeeTrainingProgress?, ErrorList>(null);
        var restored = Restore(row);
        return restored.IsFailure ? restored.Error : Result.Success<StoredEmployeeTrainingProgress?, ErrorList>(restored.Value);
    }

    /// <summary>Compare-and-swap prevents a stale snapshot overwriting later progress; identical current state is safe to replay.</summary>
    public async Task<Result<StoredEmployeeTrainingProgress, ErrorList>> SaveProgressAsync(EmployeeTrainingProgress progress, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRevision < 0 || expectedRevision == long.MaxValue) return Conflict();
        var validation = EmployeeTrainingRules.RestoreProgress(progress.StaffId, progress.ScenarioId, progress.Version, progress.CompletedVersion,
            progress.Status, progress.NextStep, progress.DemoState, progress.IsRunning, progress.WasDeferred, progress.AutoStartSkippedForOpenForm);
        if (validation.IsFailure) return validation.Error;
        var sql = expectedRevision == 0 ? $"""
            INSERT INTO public.employee_training_progress(staff_id, scenario_id, version, completed_version, status, next_step,
                demo_state_schema_version, demo_state, is_running, was_deferred, auto_start_skipped_for_open_form, revision)
            VALUES (@staffId, @scenarioId, @version, @completedVersion, CAST(@status AS public.employee_training_status), @nextStep,
                1, CAST(@demoState AS jsonb), @isRunning, @wasDeferred, @skippedForm, 1)
            ON CONFLICT (staff_id, scenario_id) DO NOTHING
            RETURNING {ProgressColumns}
            """ : $"""
            UPDATE public.employee_training_progress SET version = @version, completed_version = @completedVersion,
                status = CAST(@status AS public.employee_training_status), next_step = @nextStep, demo_state_schema_version = 1,
                demo_state = CAST(@demoState AS jsonb), is_running = @isRunning, was_deferred = @wasDeferred,
                auto_start_skipped_for_open_form = @skippedForm, revision = revision + 1
            WHERE staff_id = @staffId AND scenario_id = @scenarioId AND revision = @expectedRevision
            RETURNING {ProgressColumns}
            """;
        var row = await database.Query(sql).WithParameter("staffId", progress.StaffId.Value).WithParameter("scenarioId", progress.ScenarioId)
            .WithParameter("version", progress.Version).WithParameter("completedVersion", progress.CompletedVersion)
            .WithParameter("status", StatusLabel(progress.Status)).WithParameter("nextStep", progress.NextStep)
            .WithParameter("demoState", JsonSerializer.Serialize(progress.DemoState))
            .WithParameter("isRunning", progress.IsRunning).WithParameter("wasDeferred", progress.WasDeferred)
            .WithParameter("skippedForm", progress.AutoStartSkippedForOpenForm).WithParameter("expectedRevision", expectedRevision)
            .GetSingleOrDefaultAsync<ProgressRow>(cancellationToken);
        if (row is not null) return Restore(row);
        var current = await ReadProgressAsync(progress.StaffId, progress.ScenarioId, cancellationToken);
        if (current.IsFailure) return current.Error;
        return current.Value is { } stored && SameProgress(stored.Progress, progress) ? stored : Conflict();
    }

    /// <summary>Reads article progress separately from interactive exercise completion.</summary>
    public async Task<Result<StoredEmployeeTrainingArticle?, ErrorList>> ReadArticleAsync(StaffId staffId, string articleId, CancellationToken cancellationToken)
    {
        var row = await database.Query($"SELECT {ArticleColumns} FROM public.employee_training_article_progress WHERE staff_id = @staffId AND article_id = @articleId")
            .WithParameter("staffId", staffId.Value).WithParameter("articleId", articleId).GetSingleOrDefaultAsync<ArticleRow>(cancellationToken);
        if (row is null) return Result.Success<StoredEmployeeTrainingArticle?, ErrorList>(null);
        var restored = Restore(row);
        return restored.IsFailure ? restored.Error : Result.Success<StoredEmployeeTrainingArticle?, ErrorList>(restored.Value);
    }

    /// <summary>Saves only the selected employee/article with an explicit revision precondition.</summary>
    public async Task<Result<StoredEmployeeTrainingArticle, ErrorList>> SaveArticleAsync(EmployeeTrainingArticleProgress progress, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (expectedRevision < 0 || expectedRevision == long.MaxValue) return Conflict();
        var validation = EmployeeTrainingArticleRules.Restore(progress.StaffId, progress.ArticleId, progress.Version, progress.ReadVersion, progress.HasNewUpdate);
        if (validation.IsFailure) return validation.Error;
        var sql = expectedRevision == 0 ? $"""
            INSERT INTO public.employee_training_article_progress(staff_id, article_id, version, read_version, has_new_update, revision)
            VALUES (@staffId, @articleId, @version, @readVersion, @hasNewUpdate, 1)
            ON CONFLICT (staff_id, article_id) DO NOTHING
            RETURNING {ArticleColumns}
            """ : $"""
            UPDATE public.employee_training_article_progress SET version = @version, read_version = @readVersion,
                has_new_update = @hasNewUpdate, revision = revision + 1
            WHERE staff_id = @staffId AND article_id = @articleId AND revision = @expectedRevision
            RETURNING {ArticleColumns}
            """;
        var row = await database.Query(sql).WithParameter("staffId", progress.StaffId.Value).WithParameter("articleId", progress.ArticleId)
            .WithParameter("version", progress.Version).WithParameter("readVersion", progress.ReadVersion)
            .WithParameter("hasNewUpdate", progress.HasNewUpdate).WithParameter("expectedRevision", expectedRevision)
            .GetSingleOrDefaultAsync<ArticleRow>(cancellationToken);
        if (row is not null) return Restore(row);
        var current = await ReadArticleAsync(progress.StaffId, progress.ArticleId, cancellationToken);
        if (current.IsFailure) return current.Error;
        return current.Value is { } stored && stored.Progress == progress ? stored : Conflict();
    }

    private static ErrorList Conflict() => ErrorList.Single(new ModelError.InvalidState(), "revision");

    private static string StatusLabel(EmployeeTrainingStatus status) => status switch
    {
        EmployeeTrainingStatus.NotStarted => "not_started",
        EmployeeTrainingStatus.InProgress => "in_progress",
        EmployeeTrainingStatus.Completed => "completed",
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    private static Result<StoredEmployeeTrainingProgress, ErrorList> Restore(ProgressRow row)
    {
        if (row.DemoStateSchemaVersion != 1 || row.Revision <= 0) return ErrorList.Single(new ModelError.InvalidState(), "progressFormat");
        var status = row.Status switch
        {
            "not_started" => EmployeeTrainingStatus.NotStarted,
            "in_progress" => EmployeeTrainingStatus.InProgress,
            "completed" => EmployeeTrainingStatus.Completed,
            _ => (EmployeeTrainingStatus)(-1)
        };
        ImmutableDictionary<string, string>? demo;
        try { demo = JsonSerializer.Deserialize<ImmutableDictionary<string, string>>(row.DemoState); }
        catch (JsonException) { return ErrorList.Single(new ModelError.InvalidState(), "demoState"); }
        if (demo is null || demo.Values.Any(value => value is null)) return ErrorList.Single(new ModelError.InvalidState(), "demoState");
        var restored = EmployeeTrainingRules.RestoreProgress(new(row.StaffId), row.ScenarioId, row.Version, row.CompletedVersion,
            status, row.NextStep, demo, row.IsRunning, row.WasDeferred, row.AutoStartSkippedForOpenForm);
        return restored.IsFailure ? restored.Error : new StoredEmployeeTrainingProgress(restored.Value, row.Revision);
    }

    private static Result<StoredEmployeeTrainingArticle, ErrorList> Restore(ArticleRow row)
    {
        if (row.Revision <= 0) return ErrorList.Single(new ModelError.InvalidState(), "revision");
        var restored = EmployeeTrainingArticleRules.Restore(new(row.StaffId), row.ArticleId, row.Version, row.ReadVersion, row.HasNewUpdate);
        return restored.IsFailure ? restored.Error : new StoredEmployeeTrainingArticle(restored.Value, row.Revision);
    }

    private static bool SameProgress(EmployeeTrainingProgress left, EmployeeTrainingProgress right) =>
        left.StaffId == right.StaffId && left.ScenarioId == right.ScenarioId && left.Version == right.Version &&
        left.CompletedVersion == right.CompletedVersion && left.Status == right.Status && left.NextStep == right.NextStep &&
        left.IsRunning == right.IsRunning && left.WasDeferred == right.WasDeferred && left.AutoStartSkippedForOpenForm == right.AutoStartSkippedForOpenForm &&
        left.DemoState.Count == right.DemoState.Count && left.DemoState.All(pair => right.DemoState.TryGetValue(pair.Key, out var value) && pair.Value == value);

    private sealed class ProgressRow
    {
        public Guid StaffId { get; init; }
        public string ScenarioId { get; init; } = "";
        public int Version { get; init; }
        public int? CompletedVersion { get; init; }
        public string Status { get; init; } = "";
        public int NextStep { get; init; }
        public int DemoStateSchemaVersion { get; init; }
        public string DemoState { get; init; } = "";
        public bool IsRunning { get; init; }
        public bool WasDeferred { get; init; }
        public bool AutoStartSkippedForOpenForm { get; init; }
        public long Revision { get; init; }
    }

    private sealed class ArticleRow
    {
        public Guid StaffId { get; init; }
        public string ArticleId { get; init; } = "";
        public int Version { get; init; }
        public int? ReadVersion { get; init; }
        public bool HasNewUpdate { get; init; }
        public long Revision { get; init; }
    }
}
