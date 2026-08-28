#!/usr/bin/env dotnet
#:property TargetFramework=net10.0

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var repositoryRoot = FindRepositoryRoot();
var command = args.FirstOrDefault() ?? "help";
var changelog = Changelog.Load(Path.Combine(repositoryRoot, "changelog", "releases"));

switch (command)
{
    case "validate":
        Console.Write(Changelog.Render(changelog.Current));
        if (changelog.Draft is not null)
        {
            Console.WriteLine($"Draft: {changelog.Draft.Version}");
        }
        break;
    case "current-version":
        Console.Write(changelog.Current.Version);
        break;
    case "release-kind":
        Console.Write(GetReleaseMerge(repositoryRoot, changelog.Current) is { } releaseMerge
            ? releaseMerge.GetProperty("head").GetProperty("ref").GetString()!.Split('/')[0]
            : "none");
        break;
    case "start-next-release":
        StartNextRelease(repositoryRoot, changelog, args.Skip(1).ToArray());
        break;
    case "start-hotfix":
        StartHotfix(repositoryRoot, changelog);
        break;
    case "prepare":
        PrepareRelease(repositoryRoot, changelog, args.Skip(1).ToArray());
        break;
    case "finalize":
        FinalizeRelease(repositoryRoot);
        break;
    case "publish":
        PublishRelease(repositoryRoot, changelog.Current);
        break;
    case "self-test":
        RunSelfTests();
        break;
    default:
        Console.WriteLine("Usage: dotnet run scripts/ReleaseTool.cs -- validate|current-version|release-kind|start-next-release <codename> [version]|start-hotfix|prepare [next-codename] [next-version]|finalize|publish|self-test");
        break;
}

static void StartNextRelease(string repositoryRoot, Changelog changelog, string[] options)
{
    if (options.Length is < 1 or > 2)
    {
        throw new InvalidOperationException("start-next-release requires a codename and accepts an optional explicit version.");
    }

    EnsureCleanBranch(repositoryRoot, "develop");
    var repository = new Repository("monorepo", repositoryRoot);
    FetchReleaseRefs(repository);
    Run("git", ["merge", "--ff-only", "origin/develop"], repositoryRoot);
    changelog = Changelog.Load(Path.Combine(repositoryRoot, "changelog", "releases"));
    CreateDraftFile(repositoryRoot, changelog, options[0], options.ElementAtOrDefault(1));
    var draft = Changelog.Load(Path.Combine(repositoryRoot, "changelog", "releases")).Draft!;
    CommitReleaseMetadata(repositoryRoot, $"start {draft.Version} release draft");
    Console.WriteLine($"Created regular release draft {draft.Version} ({draft.Codename}).");
}

static void StartHotfix(string repositoryRoot, Changelog changelog)
{
    EnsureCleanBranch(repositoryRoot, "master");
    if (changelog.Draft is not null)
    {
        throw new InvalidOperationException($"Draft {changelog.Draft.Version} already exists.");
    }

    var repository = new Repository("monorepo", repositoryRoot);
    FetchReleaseRefs(repository);
    Run("git", ["merge", "--ff-only", "origin/master"], repositoryRoot);
    changelog = Changelog.Load(Path.Combine(repositoryRoot, "changelog", "releases"));
    var version = AllocateHotfixVersion(changelog);
    var branch = $"hotfix/{version}";
    if (HasRef(repositoryRoot, $"refs/heads/{branch}") || HasRef(repositoryRoot, $"refs/remotes/origin/{branch}"))
    {
        throw new InvalidOperationException($"{branch} already exists.");
    }

    Run("git", ["switch", "--create", branch], repositoryRoot);
    WriteDraft(repositoryRoot, version, codename: null);
    CommitReleaseMetadata(repositoryRoot, $"start {version} hotfix draft");
    Console.WriteLine($"Created {branch} from current master.");
}

static void CreateDraftFile(string repositoryRoot, Changelog changelog, string codename, string? explicitVersion)
{
    if (string.IsNullOrWhiteSpace(codename))
    {
        throw new InvalidOperationException("The release codename must be non-empty.");
    }
    if (changelog.Draft is not null)
    {
        throw new InvalidOperationException($"Draft {changelog.Draft.Version} already exists.");
    }

    var version = AllocateRegularVersion(changelog, DateOnly.FromDateTime(DateTime.UtcNow), explicitVersion);
    WriteDraft(repositoryRoot, version, codename.Trim());
}

static void WriteDraft(string repositoryRoot, string version, string? codename)
{
    var root = new JsonObject
    {
        ["version"] = version,
        ["date"] = null,
        ["changes"] = new JsonObject
        {
            ["new"] = new JsonArray(),
            ["improved"] = new JsonArray(),
            ["fixed"] = new JsonArray(),
            ["security"] = new JsonArray()
        }
    };
    if (codename is not null)
    {
        root["codename"] = codename;
    }

    var path = Path.Combine(repositoryRoot, "changelog", "releases", $"{version}.json");
    if (File.Exists(path))
    {
        throw new InvalidOperationException($"{Path.GetFileName(path)} already exists.");
    }
    File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
}

static void FinalizeDraftFile(string repositoryRoot, ReleaseEntry finalized)
{
    var path = Path.Combine(repositoryRoot, "changelog", "releases", $"{finalized.Version}.json");
    var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException($"{Path.GetFileName(path)} must be an object.");
    root["date"] = finalized.Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
}

static void CommitReleaseMetadata(string repositoryRoot, string subject)
{
    Run("git", ["add", "--", "changelog/releases"], repositoryRoot);
    Run("git", ["commit", "--message", subject], repositoryRoot);
}

static string AllocateRegularVersion(Changelog changelog, DateOnly today, string? explicitVersion)
{
    if (explicitVersion is not null)
    {
        if (!Changelog.TryParseVersion(explicitVersion, out var explicitParts) || explicitParts.Patch is not null)
        {
            throw new InvalidOperationException("An explicit regular version must use YYYY.MM.N.");
        }
        EnsureUnusedVersion(changelog, explicitVersion);
        if (CompareVersionParts(explicitParts, Changelog.ParseVersion(changelog.Current.Version)) <= 0)
        {
            throw new InvalidOperationException("An explicit regular version must be newer than the current production version.");
        }
        return explicitVersion;
    }

    var next = changelog.Releases
        .Select(entry => Changelog.ParseVersion(entry.Version))
        .Where(version => version.Year == today.Year && version.Month == today.Month && version.Patch is null)
        .Select(version => version.Release)
        .DefaultIfEmpty(0)
        .Max() + 1;
    var version = $"{today.Year:D4}.{today.Month:D2}.{next}";
    if (CompareVersionParts(Changelog.ParseVersion(version), Changelog.ParseVersion(changelog.Current.Version)) <= 0)
    {
        throw new InvalidOperationException(
            $"Automatically allocated version {version} is not newer than current production {changelog.Current.Version}; pass an explicit future version.");
    }

    return version;
}

static int CompareVersionParts(VersionParts left, VersionParts right)
{
    var leftValues = new[] { left.Year, left.Month, left.Release, left.Patch ?? 0 };
    var rightValues = new[] { right.Year, right.Month, right.Release, right.Patch ?? 0 };
    for (var index = 0; index < leftValues.Length; index++)
    {
        var comparison = leftValues[index].CompareTo(rightValues[index]);
        if (comparison != 0)
        {
            return comparison;
        }
    }
    return 0;
}

static void RunSelfTests()
{
    var directory = Path.Combine(Path.GetTempPath(), $"melodytrack-release-tool-{Guid.NewGuid():N}");
    Directory.CreateDirectory(directory);
    try
    {
        File.WriteAllText(
            Path.Combine(directory, "2026.08.1.json"),
            """
            {"version":"2026.08.1","codename":"Cantabile","date":"2026-08-01","changes":{"new":["Release"],"improved":[],"fixed":[],"security":[]}}
            """);
        File.WriteAllText(
            Path.Combine(directory, "2026.08.1.1.json"),
            """
            {"version":"2026.08.1.1","date":"2026-08-02","changes":{"new":[],"improved":[],"fixed":["Hotfix"],"security":[]}}
            """);

        var changelog = Changelog.Load(directory);
        AssertEqual("2026.08.2", AllocateRegularVersion(changelog, new DateOnly(2026, 8, 27), null), "regular version allocation");
        AssertEqual("2026.08.1.2", AllocateHotfixVersion(changelog), "hotfix version allocation");
        AssertEqual("2026.09.1", AllocateRegularVersion(changelog, new DateOnly(2026, 8, 27), "2026.09.1"), "explicit version allocation");
        AssertEqual(MergeBackAction.DeleteMerged, DetermineMergeBackAction("hotfix/2026.08.1.2", true), "merged hotfix cleanup");
        AssertEqual(MergeBackAction.UpdateActiveRelease, DetermineMergeBackAction("release/2026.09.1", false), "active release merge-back");
        AssertEqual(MergeBackAction.KeepActiveHotfix, DetermineMergeBackAction("hotfix/2026.08.1.3", false), "active hotfix preservation");

        File.WriteAllText(
            Path.Combine(directory, "2026.08.2.json"),
            """
            {"version":"2026.08.2","codename":"Dolce","date":null,"changes":{"new":[],"improved":[],"fixed":[],"security":[]}}
            """);
        AssertEqual("2026.08.2", Changelog.Load(directory).Draft?.Version, "draft parsing");
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }

    Console.WriteLine("ReleaseTool self-tests passed.");
}

static void AssertEqual<T>(T expected, T actual, string label)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{label}: expected {expected}, received {actual}.");
    }
}

static string AllocateHotfixVersion(Changelog changelog)
{
    var production = changelog.Current;
    var parentVersion = production.ParentVersion ?? production.Version;
    var parent = changelog.Releases.SingleOrDefault(entry => entry.Version == parentVersion)
        ?? throw new InvalidOperationException($"Production parent {parentVersion} is missing.");
    if (parent.IsDraft || parent.IsPatch)
    {
        throw new InvalidOperationException("The production hotfix parent must be a released regular version.");
    }

    var nextPatch = changelog.Releases
        .Where(entry => entry.ParentVersion == parentVersion)
        .Select(entry => Changelog.ParseVersion(entry.Version).Patch!.Value)
        .DefaultIfEmpty(0)
        .Max() + 1;
    return $"{parentVersion}.{nextPatch}";
}

static void EnsureUnusedVersion(Changelog changelog, string version)
{
    if (changelog.Releases.Any(entry => entry.Version == version))
    {
        throw new InvalidOperationException($"Version {version} already exists.");
    }
}

static void EnsureCleanBranch(string repositoryRoot, string expectedBranch)
{
    if (Output("git", ["status", "--porcelain"], repositoryRoot).Length > 0)
    {
        throw new InvalidOperationException("The monorepo worktree must be clean.");
    }
    var branch = Output("git", ["branch", "--show-current"], repositoryRoot);
    if (branch != expectedBranch)
    {
        throw new InvalidOperationException($"Run this command with {expectedBranch} checked out.");
    }
}

static void PrepareRelease(string repositoryRoot, Changelog changelog, string[] options)
{
    var draft = changelog.Draft ?? throw new InvalidOperationException("An active release or hotfix draft with date: null is required.");
    if (draft.Changes.Values.Sum(items => items.Count) == 0)
    {
        throw new InvalidOperationException($"Draft {draft.Version} must contain at least one release-note item before preparation.");
    }
    var finalized = draft.WithDate(DateOnly.FromDateTime(DateTime.UtcNow));
    var branchPrefix = draft.IsPatch ? "hotfix" : "release";
    var branch = $"{branchPrefix}/{draft.Version}";
    var expectedBody = Changelog.Render(finalized).Trim();
    if (!draft.IsPatch && options.Length is < 1 or > 2)
    {
        throw new InvalidOperationException("Regular release preparation requires the next release codename and accepts an optional explicit version.");
    }
    if (draft.IsPatch && options.Length != 0)
    {
        throw new InvalidOperationException("Hotfix preparation does not accept next-release arguments.");
    }
    var repository = new Repository("monorepo", repositoryRoot);
    var createdLocally = new HashSet<string>(StringComparer.Ordinal);
    var pushedThisRun = false;

    try
    {
        Run("gh", ["auth", "status"], repositoryRoot);
        EnsureCleanSource(repository, draft);
        repository.OriginalBranch = Output("git", ["branch", "--show-current"], repository.Path);
        Console.WriteLine($"{repository.Name}: checking {repository.OriginalBranch} against the remote...");
        FetchReleaseRefs(repository);
        if (!draft.IsPatch)
        {
            EnsureAncestor(repository.Path, "origin/develop", "HEAD", "origin/develop");
        }
        repository.SourceCommit = Output("git", ["rev-parse", "HEAD"], repository.Path);
        Run("git", ["merge-tree", "--write-tree", "--quiet", "origin/master", "HEAD"], repository.Path);
        if (!draft.IsPatch && HasRef(repository.Path, $"refs/heads/{branch}"))
        {
            throw new InvalidOperationException($"{repository.Name}: local {branch} already exists.");
        }

        repository.RemoteExists = HasRef(repository.Path, $"refs/remotes/origin/{branch}");
        Console.WriteLine($"{repository.Name}: checking for an existing release pull request...");
        repository.PullRequest = FindPullRequest(repository.Path, branch);
        if (repository.PullRequest is not null
            && (repository.PullRequest.Title != draft.Version || NormalizeBody(repository.PullRequest.Body) != expectedBody))
        {
            throw new InvalidOperationException($"{repository.Name}: existing pull request conflicts with the changelog.");
        }

        if (draft.IsPatch)
        {
            if (repository.OriginalBranch != branch)
            {
                throw new InvalidOperationException($"{repository.Name}: hotfix preparation must run from {branch}.");
            }

            if (repository.RemoteExists)
            {
                EnsureAncestor(repository.Path, repository.SourceCommit!, $"origin/{branch}", "hotfix source commit");
                Run("git", ["merge", "--ff-only", $"origin/{branch}"], repository.Path);
            }
            else
            {
                FinalizeDraftFile(repositoryRoot, finalized);
                CommitReleaseMetadata(repositoryRoot, $"finalize hotfix {draft.Version}");
            }
        }
        else if (repository.RemoteExists)
        {
            EnsureAncestor(repository.Path, repository.SourceCommit!, $"origin/{branch}", "source commit");
            EnsureAncestor(repository.Path, "origin/master", $"origin/{branch}", "master");
            Run("git", ["switch", "--create", branch, $"origin/{branch}"], repository.Path);
        }
        else
        {
            Run("git", ["switch", "--no-track", "--create", branch, "origin/master"], repository.Path);
            Run("git", ["merge", "--no-ff", "--no-edit", repository.SourceCommit!], repository.Path);
            FinalizeDraftFile(repositoryRoot, finalized);
            CommitReleaseMetadata(repositoryRoot, $"finalize release {draft.Version}");
        }

        createdLocally.Add(repository.Path);
        VerifyRepository(repositoryRoot);
        if (Output("git", ["status", "--porcelain"], repository.Path).Length > 0)
        {
            throw new InvalidOperationException($"{repository.Name}: verification changed the worktree.");
        }

        if (!repository.RemoteExists)
        {
            Run("git", ["push", "--set-upstream", "origin", branch], repository.Path);
            pushedThisRun = true;
        }

        var bodyFile = Path.Combine(Path.GetTempPath(), $"melodytrack-release-{Guid.NewGuid():N}.md");
        File.WriteAllText(bodyFile, $"{expectedBody}\n", new UTF8Encoding(false));
        try
        {
            var url = repository.PullRequest?.Url
                ?? Output("gh", ["pr", "create", "--base", "master", "--head", branch, "--title", draft.Version, "--body-file", bodyFile], repository.Path);
            Console.WriteLine($"{repository.Name}: {url}");
        }
        finally
        {
            File.Delete(bodyFile);
        }
    }
    finally
    {
        if (repository.OriginalBranch is not null)
        {
            try
            {
                if (Output("git", ["branch", "--show-current"], repository.Path) != repository.OriginalBranch)
                {
                    Run("git", ["switch", repository.OriginalBranch!], repository.Path);
                }
                if (!draft.IsPatch && !pushedThisRun && createdLocally.Contains(repository.Path) && HasRef(repository.Path, $"refs/heads/{branch}"))
                {
                    Run("git", ["branch", "--delete", "--force", branch], repository.Path);
                }
            }
            catch (Exception exception)
            {
                Console.Error.WriteLine($"Could not restore {repository.Name}: {exception.Message}");
            }
        }
    }

    if (!draft.IsPatch)
    {
        FinalizeDraftFile(repositoryRoot, finalized);
        var refreshed = Changelog.Load(Path.Combine(repositoryRoot, "changelog", "releases"));
        CreateDraftFile(repositoryRoot, refreshed, options[0], options.ElementAtOrDefault(1));
        CommitReleaseMetadata(repositoryRoot, $"start {Changelog.Load(Path.Combine(repositoryRoot, "changelog", "releases")).Draft!.Version} release draft");
    }
}

static void FinalizeRelease(string repositoryRoot)
{
    var repository = new Repository("monorepo", repositoryRoot);
    EnsureFinalizeSource(repository);
    Console.WriteLine($"{repository.Name}: checking merged master state...");
    FetchReleaseRefs(repository);
    EnsureAncestor(repository.Path, "master", "origin/master", "local master");
    repository.LocalReleaseBranches = GetLocalReleaseBranches(repository.Path);
    var localHotfixBranches = GetLocalHotfixBranches(repository.Path);

    Run("git", ["switch", "master"], repository.Path);
    Run("git", ["merge", "--ff-only", "origin/master"], repository.Path);
    if (Output("git", ["rev-parse", "master"], repository.Path) != Output("git", ["rev-parse", "origin/master"], repository.Path))
    {
        throw new InvalidOperationException($"{repository.Name}: local master does not match origin/master.");
    }

    Run("git", ["switch", "develop"], repository.Path);
    Run("git", ["merge", "--no-edit", "master"], repository.Path);

    var deletedBranches = new List<string>();
    var updatedReleaseBranches = new List<string>();
    foreach (var branch in repository.LocalReleaseBranches)
    {
        var action = DetermineMergeBackAction(branch, IsAncestor(repository.Path, branch, "master"));
        if (action == MergeBackAction.DeleteMerged)
        {
            Run("git", ["branch", "--delete", branch], repository.Path);
            deletedBranches.Add(branch);
            continue;
        }

        if (action == MergeBackAction.UpdateActiveRelease)
        {
            Run("git", ["switch", branch], repository.Path);
            Run("git", ["merge", "--no-edit", "master"], repository.Path);
            updatedReleaseBranches.Add(branch);
        }
    }
    foreach (var branch in localHotfixBranches)
    {
        if (DetermineMergeBackAction(branch, IsAncestor(repository.Path, branch, "master")) == MergeBackAction.DeleteMerged)
        {
            Run("git", ["branch", "--delete", branch], repository.Path);
            deletedBranches.Add(branch);
        }
    }
    Run("git", ["switch", "develop"], repository.Path);

    var commit = Output("git", ["rev-parse", "--short", "develop"], repository.Path);
    Console.WriteLine($"{repository.Name}: merged master into develop at {commit}; removed {deletedBranches.Count} merged branch(es) and updated {updatedReleaseBranches.Count} active release branch(es).");
}

static MergeBackAction DetermineMergeBackAction(string branch, bool mergedIntoMaster)
{
    if (mergedIntoMaster)
    {
        return MergeBackAction.DeleteMerged;
    }
    return branch.StartsWith("release/", StringComparison.Ordinal)
        ? MergeBackAction.UpdateActiveRelease
        : MergeBackAction.KeepActiveHotfix;
}

static void VerifyRepository(string repositoryRoot)
{
    Run("dotnet", ["test", "MelodyTrack.slnx", "-c", "Release"], repositoryRoot);
    Run("npm", ["run", "verify"], Path.Combine(repositoryRoot, "MelodyTrack.Web"));
}

static void PublishRelease(string repositoryRoot, ReleaseEntry current)
{
    if (current.IsDraft)
    {
        throw new InvalidOperationException("A draft release cannot be published.");
    }
    if (GetReleaseMerge(repositoryRoot, current) is null)
    {
        return;
    }

    var repository = RequiredEnvironment("GITHUB_REPOSITORY");
    var commit = RequiredEnvironment("GITHUB_SHA");
    var expectedBody = Changelog.Render(current).Trim();

    var tag = $"v{current.Version}";
    var title = $"{current.Version} — {current.ResolvedCodename}";
    Run("gh", ["auth", "setup-git"], repositoryRoot);
    Run("git", ["fetch", "--tags", "origin"], repositoryRoot);
    if (HasRef(repositoryRoot, $"refs/tags/{tag}"))
    {
        if (Output("git", ["rev-list", "-n", "1", tag], repositoryRoot) != commit)
        {
            throw new InvalidOperationException($"{tag} already points to a different commit.");
        }
    }
    else
    {
        Run(
            "git",
            [
                "-c", "user.name=github-actions[bot]",
                "-c", "user.email=41898282+github-actions[bot]@users.noreply.github.com",
                "tag", "--annotate", tag, commit, "--message", title
            ],
            repositoryRoot);
        Run("git", ["push", "origin", tag], repositoryRoot);
    }

    if (TryOutput("gh", ["release", "view", tag, "--json", "tagName,name,body"], repositoryRoot, out var releaseJson))
    {
        using var release = JsonDocument.Parse(releaseJson);
        var root = release.RootElement;
        if (root.GetProperty("tagName").GetString() != tag
            || root.GetProperty("name").GetString() != title
            || NormalizeBody(root.GetProperty("body").GetString() ?? string.Empty) != expectedBody)
        {
            throw new InvalidOperationException($"{tag} GitHub Release metadata conflicts with the changelog.");
        }

        return;
    }

    var notesFile = Path.Combine(Path.GetTempPath(), $"melodytrack-release-notes-{Guid.NewGuid():N}.md");
    File.WriteAllText(notesFile, $"{expectedBody}\n", new UTF8Encoding(false));
    try
    {
        Run("gh", ["release", "create", tag, "--verify-tag", "--title", title, "--notes-file", notesFile], repositoryRoot);
    }
    finally
    {
        File.Delete(notesFile);
    }
}

static JsonElement? GetReleaseMerge(string repositoryRoot, ReleaseEntry current)
{
    var repository = RequiredEnvironment("GITHUB_REPOSITORY");
    var commit = RequiredEnvironment("GITHUB_SHA");
    using var pulls = JsonDocument.Parse(Output("gh", ["api", $"repos/{repository}/commits/{commit}/pulls"], repositoryRoot));
    var candidates = pulls.RootElement.EnumerateArray().Where(pull =>
        pull.GetProperty("merged_at").ValueKind != JsonValueKind.Null
        && pull.GetProperty("base").GetProperty("ref").GetString() == "master"
        && (pull.GetProperty("head").GetProperty("ref").GetString()?.StartsWith("release/", StringComparison.Ordinal) == true
            || pull.GetProperty("head").GetProperty("ref").GetString()?.StartsWith("hotfix/", StringComparison.Ordinal) == true)).ToArray();
    if (candidates.Length == 0)
    {
        return null;
    }
    if (candidates.Length != 1)
    {
        throw new InvalidOperationException("Merge commit is associated with multiple release pull requests.");
    }

    var pull = candidates[0];
    var branchPrefix = current.IsPatch ? "hotfix" : "release";
    var expectedBody = Changelog.Render(current).Trim();
    if (pull.GetProperty("title").GetString()?.Trim() != current.Version
        || pull.GetProperty("head").GetProperty("ref").GetString() != $"{branchPrefix}/{current.Version}"
        || NormalizeBody(pull.GetProperty("body").GetString() ?? string.Empty) != expectedBody)
    {
        throw new InvalidOperationException("Release pull request does not match the current changelog entry.");
    }
    return pull.Clone();
}

static string FindRepositoryRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "MelodyTrack.slnx"))
            && Directory.Exists(Path.Combine(directory.FullName, "MelodyTrack.Web"))
            && Directory.Exists(Path.Combine(directory.FullName, "changelog", "releases")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Run this application from the MelodyTrack monorepo.");
}

static string NormalizeBody(string value) => value.Trim().TrimStart('\uFEFF');

static void FetchReleaseRefs(Repository repository) =>
    Run(
        "git",
        [
            "fetch", "--prune", "origin",
            "+refs/heads/master:refs/remotes/origin/master",
            "+refs/heads/develop:refs/remotes/origin/develop",
            "+refs/heads/release/*:refs/remotes/origin/release/*",
            "+refs/heads/hotfix/*:refs/remotes/origin/hotfix/*"
        ],
        repository.Path);

static void EnsureCleanSource(Repository repository, ReleaseEntry draft)
{
    if (!Directory.Exists(Path.Combine(repository.Path, ".git")))
    {
        throw new InvalidOperationException($"{repository.Name}: repository was not found at {repository.Path}.");
    }

    if (Output("git", ["status", "--porcelain"], repository.Path).Length > 0)
    {
        throw new InvalidOperationException($"{repository.Name}: worktree must be clean.");
    }

    var branch = Output("git", ["branch", "--show-current"], repository.Path);
    var expectedBranch = draft.IsPatch ? $"hotfix/{draft.Version}" : "develop";
    if (branch != expectedBranch)
    {
        throw new InvalidOperationException($"{repository.Name}: run from {expectedBranch}.");
    }
}

static void EnsureFinalizeSource(Repository repository)
{
    if (!Directory.Exists(Path.Combine(repository.Path, ".git")))
    {
        throw new InvalidOperationException($"{repository.Name}: repository was not found at {repository.Path}.");
    }

    if (Output("git", ["status", "--porcelain"], repository.Path).Length > 0)
    {
        throw new InvalidOperationException($"{repository.Name}: worktree must be clean.");
    }

    if (Output("git", ["branch", "--show-current"], repository.Path) != "develop")
    {
        throw new InvalidOperationException($"{repository.Name}: finalize must run with develop checked out.");
    }

    foreach (var branch in new[] { "master", "develop" })
    {
        if (!HasRef(repository.Path, $"refs/heads/{branch}"))
        {
            throw new InvalidOperationException($"{repository.Name}: local {branch} branch does not exist.");
        }
    }
}

static string[] GetLocalReleaseBranches(string workingDirectory) =>
    Output("git", ["for-each-ref", "--format=%(refname:short)", "refs/heads/release/"], workingDirectory)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

static string[] GetLocalHotfixBranches(string workingDirectory) =>
    Output("git", ["for-each-ref", "--format=%(refname:short)", "refs/heads/hotfix/"], workingDirectory)
        .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

static PullRequest? FindPullRequest(string workingDirectory, string branch)
{
    using var document = JsonDocument.Parse(Output(
        "gh",
        ["pr", "list", "--state", "open", "--base", "master", "--head", branch, "--json", "url,title,body"],
        workingDirectory));
    var pulls = document.RootElement.EnumerateArray().ToArray();
    if (pulls.Length > 1)
    {
        throw new InvalidOperationException($"Multiple pull requests exist for {branch}.");
    }

    return pulls.Length == 0
        ? null
        : new PullRequest(
            pulls[0].GetProperty("url").GetString()!,
            pulls[0].GetProperty("title").GetString()!,
            pulls[0].GetProperty("body").GetString() ?? string.Empty);
}

static void EnsureAncestor(string workingDirectory, string ancestor, string descendant, string label)
{
    if (RunForExitCode("git", ["merge-base", "--is-ancestor", ancestor, descendant], workingDirectory) != 0)
    {
        throw new InvalidOperationException($"{descendant} does not contain the captured {label}.");
    }
}

static bool IsAncestor(string workingDirectory, string ancestor, string descendant) =>
    RunForExitCode("git", ["merge-base", "--is-ancestor", ancestor, descendant], workingDirectory) == 0;

static bool HasRef(string workingDirectory, string reference) =>
    RunForExitCode("git", ["show-ref", "--verify", "--quiet", reference], workingDirectory) == 0;

static string RequiredEnvironment(string name) =>
    Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
        ? value
        : throw new InvalidOperationException($"{name} is required.");

static string Output(string command, IReadOnlyList<string> arguments, string workingDirectory)
{
    var result = Execute(command, arguments, workingDirectory, captureOutput: true);
    if (result.ExitCode != 0)
    {
        throw new InvalidOperationException($"{command} exited with code {result.ExitCode}.");
    }

    return result.Output.Trim();
}

static bool TryOutput(string command, IReadOnlyList<string> arguments, string workingDirectory, out string output)
{
    var result = Execute(command, arguments, workingDirectory, captureOutput: true);
    output = result.Output.Trim();
    return result.ExitCode == 0;
}

static void Run(string command, IReadOnlyList<string> arguments, string workingDirectory)
{
    if (RunForExitCode(command, arguments, workingDirectory) != 0)
    {
        throw new InvalidOperationException($"{command} failed.");
    }
}

static int RunForExitCode(string command, IReadOnlyList<string> arguments, string workingDirectory) =>
    Execute(command, arguments, workingDirectory, captureOutput: false).ExitCode;

static ProcessResult Execute(string command, IReadOnlyList<string> arguments, string workingDirectory, bool captureOutput)
{
    var startInfo = new ProcessStartInfo(command) { WorkingDirectory = workingDirectory, UseShellExecute = false };
    startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
    startInfo.Environment["GH_PROMPT_DISABLED"] = "1";
    foreach (var argument in arguments)
    {
        startInfo.ArgumentList.Add(argument);
    }

    if (captureOutput)
    {
        startInfo.RedirectStandardOutput = true;
    }

    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {command}.");
    var output = captureOutput ? process.StandardOutput.ReadToEnd() : string.Empty;
    process.WaitForExit();
    return new ProcessResult(process.ExitCode, output);
}

sealed class Changelog
{
    private static readonly Regex VersionPattern = new(
        @"^(?<year>\d{4})\.(?<month>0[1-9]|1[0-2])\.(?<release>[1-9]\d*)(?:\.(?<patch>[1-9]\d*))?$",
        RegexOptions.CultureInvariant);
    private static readonly string[] Categories = ["new", "improved", "fixed", "security"];
    private static readonly IReadOnlyDictionary<string, string> Headings = new Dictionary<string, string>
    {
        ["new"] = "Новое",
        ["improved"] = "Улучшения",
        ["fixed"] = "Исправления",
        ["security"] = "Безопасность"
    };

    private Changelog(IReadOnlyList<ReleaseEntry> releases) => Releases = releases;
    public IReadOnlyList<ReleaseEntry> Releases { get; }
    public ReleaseEntry Current => Releases.First(entry => !entry.IsDraft);
    public ReleaseEntry? Draft => Releases.SingleOrDefault(entry => entry.IsDraft);

    public static Changelog Load(string releasesDirectory)
    {
        if (!Directory.Exists(releasesDirectory)) throw new DirectoryNotFoundException($"Release changelog directory was not found: {releasesDirectory}");
        var paths = Directory.EnumerateFiles(releasesDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0) throw new InvalidDataException("The release changelog must contain at least one JSON file.");

        var releases = paths.Select(path =>
        {
            var filename = Path.GetFileName(path);
            var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException($"{filename} must be an object.");
            var release = Parse(root, filename);
            if (!string.Equals(Path.GetFileNameWithoutExtension(path), release.Version, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{filename} must match release version {release.Version}.");
            }

            return release;
        }).ToList();
        if (releases.Select(entry => entry.Version).Distinct(StringComparer.Ordinal).Count() != releases.Count) throw new InvalidDataException("Release versions must be unique.");
        if (releases.Count(entry => entry.IsDraft) > 1) throw new InvalidDataException("Exactly zero or one active changelog draft is allowed.");
        if (releases.All(entry => entry.IsDraft)) throw new InvalidDataException("The changelog must contain at least one released entry.");
        releases.Sort((left, right) =>
        {
            if (left.IsDraft != right.IsDraft) return left.IsDraft ? 1 : -1;
            var dateComparison = Nullable.Compare(right.Date, left.Date);
            return dateComparison != 0 ? dateComparison : Compare(right.Version, left.Version);
        });

        if (releases.SingleOrDefault(entry => entry.IsDraft) is { } draft)
        {
            var production = releases.First(entry => !entry.IsDraft);
            var productionParent = production.ParentVersion ?? production.Version;
            if (draft.ParentVersion is not null && draft.ParentVersion != productionParent)
            {
                throw new InvalidDataException($"Draft patch {draft.Version} must target current production parent {productionParent}.");
            }

            if (Compare(draft.Version, production.Version) <= 0)
            {
                throw new InvalidDataException($"Draft {draft.Version} must be newer than current production {production.Version}.");
            }
        }

        var byVersion = releases.ToDictionary(entry => entry.Version, StringComparer.Ordinal);
        foreach (var patch in releases.Where(entry => entry.ParentVersion is not null))
        {
            if (!byVersion.TryGetValue(patch.ParentVersion!, out var parent) || parent.ParentVersion is not null) throw new InvalidDataException($"Patch {patch.Version} has no actual parent.");
            patch.ResolvedCodename = parent.Codename!;
        }

        return new Changelog(releases);
    }

    public static string Render(ReleaseEntry entry)
    {
        if (entry.IsDraft) throw new InvalidOperationException($"Draft {entry.Version} cannot be rendered as release notes.");
        var builder = new StringBuilder().Append("# ").Append(entry.Version).Append(" — ").AppendLine(entry.ResolvedCodename).AppendLine().Append("Дата: ").AppendLine(entry.Date!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        foreach (var category in Categories.Where(category => entry.Changes[category].Count > 0))
        {
            builder.AppendLine().Append("## ").AppendLine(Headings[category]).AppendLine();
            foreach (var change in entry.Changes[category]) builder.Append("- ").AppendLine(change);
        }

        return builder.ToString();
    }

    private static ReleaseEntry Parse(JsonObject? value, string location)
    {
        var entry = value ?? throw new InvalidDataException($"{location} must be an object.");
        RequireKeys(entry, ["version", "codename", "date", "changes"], location, optionalCodename: true);
        var version = RequiredString(entry, "version");
        if (!VersionPattern.IsMatch(version)) throw new InvalidDataException($"{location}.version is invalid.");
        var isPatch = version.Count(character => character == '.') == 3;
        var hasCodename = entry.ContainsKey("codename");
        var codename = hasCodename ? RequiredString(entry, "codename") : null;
        if (isPatch && hasCodename) throw new InvalidDataException("Patches must omit codename.");
        if (!isPatch && codename is null) throw new InvalidDataException("Actual releases require codename.");
        DateOnly? date = null;
        if (entry["date"] is JsonValue dateValue)
        {
            if (!DateOnly.TryParseExact(dateValue.GetValue<string>(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
            {
                throw new InvalidDataException("Release date is invalid.");
            }
            date = parsedDate;
        }
        else if (entry["date"] is not null)
        {
            throw new InvalidDataException("Release date must be a date string or null.");
        }
        var changesObject = entry["changes"]?.AsObject() ?? throw new InvalidDataException("changes is required.");
        RequireKeys(changesObject, Categories, "changes");
        var changes = Categories.ToDictionary(category => category, category => changesObject[category]?.AsArray().Select(item => item?.GetValue<string>()?.Trim() ?? string.Empty).ToArray() as IReadOnlyList<string> ?? []);
        var changeCount = changes.Values.Sum(items => items.Count);
        if (changes.Values.SelectMany(items => items).Any(string.IsNullOrWhiteSpace)) throw new InvalidDataException("Changes must contain non-empty text.");
        if (date is not null && changeCount == 0) throw new InvalidDataException("Released changes must contain at least one item.");
        return new ReleaseEntry(version, codename, date, changes, isPatch ? version[..version.LastIndexOf('.')] : null) { ResolvedCodename = codename ?? string.Empty };
    }

    private static void RequireKeys(JsonObject value, IReadOnlyCollection<string> allowed, string location, bool optionalCodename = false)
    {
        var unknown = value.Select(property => property.Key).Except(allowed, StringComparer.Ordinal).FirstOrDefault();
        if (unknown is not null) throw new InvalidDataException($"{location}.{unknown} is not allowed.");
        var missing = allowed.FirstOrDefault(key => !(optionalCodename && key == "codename") && !value.ContainsKey(key));
        if (missing is not null) throw new InvalidDataException($"{location}.{missing} is required.");
    }

    private static string RequiredString(JsonObject value, string key) => value[key]?.GetValue<string>()?.Trim() is { Length: > 0 } text ? text : throw new InvalidDataException($"{key} must be a non-empty string.");
    public static bool TryParseVersion(string value, out VersionParts parts)
    {
        var match = VersionPattern.Match(value);
        if (!match.Success)
        {
            parts = default;
            return false;
        }

        parts = new VersionParts(
            int.Parse(match.Groups["year"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["month"].Value, CultureInfo.InvariantCulture),
            int.Parse(match.Groups["release"].Value, CultureInfo.InvariantCulture),
            match.Groups["patch"].Success ? int.Parse(match.Groups["patch"].Value, CultureInfo.InvariantCulture) : null);
        return true;
    }

    public static VersionParts ParseVersion(string value) =>
        TryParseVersion(value, out var parts) ? parts : throw new InvalidDataException($"Version {value} is invalid.");

    private static int Compare(string left, string right)
    {
        var first = left.Split('.').Select(int.Parse).ToArray();
        var second = right.Split('.').Select(int.Parse).ToArray();
        for (var index = 0; index < Math.Max(first.Length, second.Length); index++)
        {
            var result = (index < first.Length ? first[index] : 0).CompareTo(index < second.Length ? second[index] : 0);
            if (result != 0) return result;
        }

        return 0;
    }
}

sealed class ReleaseEntry(string version, string? codename, DateOnly? date, IReadOnlyDictionary<string, IReadOnlyList<string>> changes, string? parentVersion)
{
    public string Version { get; } = version;
    public string? Codename { get; } = codename;
    public DateOnly? Date { get; } = date;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Changes { get; } = changes;
    public string? ParentVersion { get; } = parentVersion;
    public bool IsPatch => ParentVersion is not null;
    public bool IsDraft => Date is null;
    public string ResolvedCodename { get; set; } = string.Empty;

    public ReleaseEntry WithDate(DateOnly value) =>
        new(Version, Codename, value, Changes, ParentVersion) { ResolvedCodename = this.ResolvedCodename };
}

readonly record struct VersionParts(int Year, int Month, int Release, int? Patch);
enum MergeBackAction
{
    DeleteMerged,
    UpdateActiveRelease,
    KeepActiveHotfix
}

sealed class Repository(string name, string path)
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public string? OriginalBranch { get; set; }
    public string? SourceCommit { get; set; }
    public bool RemoteExists { get; set; }
    public PullRequest? PullRequest { get; set; }
    public string[] LocalReleaseBranches { get; set; } = [];
}

sealed record PullRequest(string Url, string Title, string Body);
sealed record ProcessResult(int ExitCode, string Output);
