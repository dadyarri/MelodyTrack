using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application;

public sealed record StoredEmployeeTrainingProgress(EmployeeTrainingProgress Progress, long Revision);
public sealed record StoredEmployeeTrainingArticle(EmployeeTrainingArticleProgress Progress, long Revision);

public interface IEmployeeTrainingStore
{
    Task<Result<StoredEmployeeTrainingProgress?, ErrorList>> ReadProgressAsync(StaffId staffId, string scenarioId, CancellationToken cancellationToken);
    Task<Result<StoredEmployeeTrainingProgress, ErrorList>> SaveProgressAsync(EmployeeTrainingProgress progress, long expectedRevision, CancellationToken cancellationToken);
    Task<Result<StoredEmployeeTrainingArticle?, ErrorList>> ReadArticleAsync(StaffId staffId, string articleId, CancellationToken cancellationToken);
    Task<Result<StoredEmployeeTrainingArticle, ErrorList>> SaveArticleAsync(EmployeeTrainingArticleProgress progress, long expectedRevision, CancellationToken cancellationToken);
}

/// <summary>Evaluates the employee's current access to actual content; there is no permissive role fallback.</summary>
public interface IEmployeeTrainingAccess
{
    Task<bool> CanUseScenarioAsync(StaffId staffId, string scenarioId, CancellationToken cancellationToken);
    Task<bool> CanReadArticleAsync(StaffId staffId, string articleId, CancellationToken cancellationToken);
}

public closed record EmployeeTrainingChange
{
    public sealed record StartManually : EmployeeTrainingChange;
    public sealed record Defer : EmployeeTrainingChange;
    public sealed record SetDemoValue(string Key, string Value) : EmployeeTrainingChange;
    public sealed record CompleteStep : EmployeeTrainingChange;
    public sealed record Restart : EmployeeTrainingChange;
    public sealed record RefreshVersion : EmployeeTrainingChange;
    public sealed record AutoStart(EmployeeTrainingAutoStart Trigger, bool HasOpenWorkForm) : EmployeeTrainingChange;
}

public sealed class EmployeeTrainingService(IEmployeeTrainingStore store, IEmployeeTrainingAccess access)
{
    private static bool Owns(Staff current, Actor actor) => actor is Actor.Staff staff && staff.Id == current.Id &&
        !current.IsBlocked && !current.IsArchived && current.Id.Value != Guid.Empty;

    public async Task<Result<StoredEmployeeTrainingProgress?, ErrorList>> ReadProgressAsync(Staff current, Actor actor,
        string scenarioId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Owns(current, actor) || !await access.CanUseScenarioAsync(current.Id, scenarioId, cancellationToken))
            return ErrorList.Single(new ModelError.Forbidden());
        return await store.ReadProgressAsync(current.Id, scenarioId, cancellationToken);
    }

    public async Task<Result<StoredEmployeeTrainingProgress, ErrorList>> ApplyAsync(Staff current, Actor actor,
        EmployeeTrainingScenario scenario, long expectedRevision, EmployeeTrainingChange change, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Owns(current, actor) || !await access.CanUseScenarioAsync(current.Id, scenario.Id, cancellationToken))
            return ErrorList.Single(new ModelError.Forbidden());
        var stored = await store.ReadProgressAsync(current.Id, scenario.Id, cancellationToken);
        if (stored.IsFailure) return stored.Error;
        if (expectedRevision < 0 || (stored.Value?.Revision ?? 0) != expectedRevision)
            return ErrorList.Single(new ModelError.InvalidState(), "revision");
        var progress = stored.Value is { } previous ? Result.Success<EmployeeTrainingProgress, ErrorList>(previous.Progress)
            : EmployeeTrainingRules.CreateProgress(current.Id, scenario);
        if (progress.IsFailure) return progress.Error;
        if (progress.Value.Version != scenario.Version && change is not EmployeeTrainingChange.RefreshVersion)
            return ErrorList.Single(new ModelError.InvalidState(), "version");
        var updated = change switch
        {
            EmployeeTrainingChange.StartManually => progress.Value.StartManually(),
            EmployeeTrainingChange.Defer => progress.Value.Defer(),
            EmployeeTrainingChange.SetDemoValue value => progress.Value.ApplyDemoValue(value.Key, value.Value),
            EmployeeTrainingChange.CompleteStep => progress.Value.CompleteStep(scenario),
            EmployeeTrainingChange.Restart => progress.Value.Restart(scenario),
            EmployeeTrainingChange.RefreshVersion => progress.Value.ApplyVersion(scenario),
            EmployeeTrainingChange.AutoStart automatic => progress.Value.AutoStart(automatic.Trigger, automatic.HasOpenWorkForm)
        };
        return updated.IsFailure ? updated.Error : await store.SaveProgressAsync(updated.Value, expectedRevision, cancellationToken);
    }

    public async Task<Result<StoredEmployeeTrainingArticle?, ErrorList>> ReadArticleAsync(Staff current, Actor actor,
        string articleId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Owns(current, actor) || !await access.CanReadArticleAsync(current.Id, articleId, cancellationToken))
            return ErrorList.Single(new ModelError.Forbidden());
        return await store.ReadArticleAsync(current.Id, articleId, cancellationToken);
    }

    public async Task<Result<StoredEmployeeTrainingArticle, ErrorList>> OpenArticleAsync(Staff current, Actor actor,
        string articleId, int version, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Owns(current, actor) || !await access.CanReadArticleAsync(current.Id, articleId, cancellationToken))
            return ErrorList.Single(new ModelError.Forbidden());
        var stored = await store.ReadArticleAsync(current.Id, articleId, cancellationToken);
        if (stored.IsFailure) return stored.Error;
        if (expectedRevision < 0 || (stored.Value?.Revision ?? 0) != expectedRevision)
            return ErrorList.Single(new ModelError.InvalidState(), "revision");
        var article = stored.Value is { } previous ? previous.Progress.ApplyVersion(version)
            : EmployeeTrainingArticleRules.Create(current.Id, articleId, version);
        if (article.IsFailure) return article.Error;
        return await store.SaveArticleAsync(article.Value.OpenForReading(), expectedRevision, cancellationToken);
    }

    public async Task<Result<StoredEmployeeTrainingArticle, ErrorList>> RefreshArticleVersionAsync(Staff current, Actor actor,
        string articleId, int version, long expectedRevision, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Owns(current, actor) || !await access.CanReadArticleAsync(current.Id, articleId, cancellationToken))
            return ErrorList.Single(new ModelError.Forbidden());
        var stored = await store.ReadArticleAsync(current.Id, articleId, cancellationToken);
        if (stored.IsFailure) return stored.Error;
        if (expectedRevision < 0 || (stored.Value?.Revision ?? 0) != expectedRevision)
            return ErrorList.Single(new ModelError.InvalidState(), "revision");
        var article = stored.Value is { } previous ? previous.Progress.ApplyVersion(version)
            : EmployeeTrainingArticleRules.Create(current.Id, articleId, version);
        return article.IsFailure ? article.Error : await store.SaveArticleAsync(article.Value, expectedRevision, cancellationToken);
    }
}
