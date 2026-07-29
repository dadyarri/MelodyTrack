#!/usr/bin/env dotnet
#:property TargetFramework=net10.0

using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

var backend = FindBackendRoot();
var command = args.FirstOrDefault() ?? "help";
var changelog = Changelog.Load(Path.Combine(backend, "MelodyTrack.Backend", "changelog.json"));

switch (command)
{
    case "validate":
        Console.Write(Changelog.Render(changelog.Current));
        break;
    case "current-version":
        Console.Write(changelog.Current.Version);
        break;
    case "prepare":
        PrepareRelease(backend, changelog.Current, args.Skip(1).ToArray());
        break;
    case "publish":
        PublishRelease(backend, changelog.Current);
        break;
    default:
        Console.WriteLine("Usage: dotnet run scripts/ReleaseTool.cs -- validate|current-version|prepare|publish [--frontend <path>]");
        break;
}

static void PrepareRelease(string backend, ReleaseEntry current, string[] arguments)
{
    var frontendIndex = Array.IndexOf(arguments, "--frontend");
    var frontend = frontendIndex >= 0 && frontendIndex + 1 < arguments.Length
        ? Path.GetFullPath(arguments[frontendIndex + 1])
        : Path.GetFullPath(Path.Combine(backend, "..", "MelodyTrack.Web"));
    var branch = $"release/{current.Version}";
    var expectedBody = Changelog.Render(current).Trim();
    var repositories = new[]
    {
        new Repository("backend", backend, "dotnet", ["test", "MelodyTrack.slnx", "-c", "Release"]),
        new Repository("frontend", frontend, "npm", ["run", "verify"])
    };
    var createdLocally = new HashSet<string>(StringComparer.Ordinal);
    var pushedThisRun = false;

    try
    {
        Run("gh", ["auth", "status"], backend);
        foreach (var repository in repositories)
        {
            EnsureCleanSource(repository);
            repository.OriginalBranch = Output("git", ["branch", "--show-current"], repository.Path);
            repository.SourceCommit = Output("git", ["rev-parse", "HEAD"], repository.Path);
            Run("git", ["fetch", "origin", "master"], repository.Path);
            Run("git", ["merge-tree", "--write-tree", "--quiet", "origin/master", "HEAD"], repository.Path);
            if (HasRef(repository.Path, $"refs/heads/{branch}"))
            {
                throw new InvalidOperationException($"{repository.Name}: local {branch} already exists.");
            }

            repository.RemoteExists = HasRemoteRef(repository.Path, branch);
            repository.PullRequest = FindPullRequest(repository.Path, branch);
            if (repository.PullRequest is not null
                && (repository.PullRequest.Title != current.Version || NormalizeBody(repository.PullRequest.Body) != expectedBody))
            {
                throw new InvalidOperationException($"{repository.Name}: existing pull request conflicts with the changelog.");
            }
        }

        foreach (var repository in repositories)
        {
            if (repository.RemoteExists)
            {
                Run("git", ["fetch", "origin", $"{branch}:refs/remotes/origin/{branch}"], repository.Path);
                EnsureAncestor(repository.Path, repository.SourceCommit!, $"origin/{branch}", "source commit");
                EnsureAncestor(repository.Path, "origin/master", $"origin/{branch}", "master");
                Run("git", ["switch", "--create", branch, $"origin/{branch}"], repository.Path);
            }
            else
            {
                Run("git", ["switch", "--create", branch, "origin/master"], repository.Path);
                Run("git", ["merge", "--no-ff", "--no-edit", repository.SourceCommit!], repository.Path);
            }

            createdLocally.Add(repository.Path);
        }

        foreach (var repository in repositories)
        {
            Run(repository.VerifyCommand, repository.VerifyArguments, repository.Path);
            if (Output("git", ["status", "--porcelain"], repository.Path).Length > 0)
            {
                throw new InvalidOperationException($"{repository.Name}: verification changed the worktree.");
            }
        }

        foreach (var repository in repositories.Where(repository => !repository.RemoteExists))
        {
            Run("git", ["push", "--set-upstream", "origin", branch], repository.Path);
            pushedThisRun = true;
        }

        var bodyFile = Path.Combine(Path.GetTempPath(), $"melodytrack-release-{Guid.NewGuid():N}.md");
        File.WriteAllText(bodyFile, $"{expectedBody}\n", new UTF8Encoding(false));
        try
        {
            foreach (var repository in repositories)
            {
                var url = repository.PullRequest?.Url
                    ?? Output("gh", ["pr", "create", "--base", "master", "--head", branch, "--title", current.Version, "--body-file", bodyFile], repository.Path);
                Console.WriteLine($"{repository.Name}: {url}");
            }
        }
        finally
        {
            File.Delete(bodyFile);
        }
    }
    finally
    {
        foreach (var repository in repositories.Where(repository => repository.OriginalBranch is not null))
        {
            try
            {
                Run("git", ["switch", repository.OriginalBranch!], repository.Path);
                if (!pushedThisRun && createdLocally.Contains(repository.Path) && HasRef(repository.Path, $"refs/heads/{branch}"))
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
}

static void PublishRelease(string backend, ReleaseEntry current)
{
    var repository = RequiredEnvironment("GITHUB_REPOSITORY");
    var commit = RequiredEnvironment("GITHUB_SHA");
    using var pulls = JsonDocument.Parse(Output("gh", ["api", $"repos/{repository}/commits/{commit}/pulls"], backend));
    var candidates = pulls.RootElement.EnumerateArray().Where(pull =>
        pull.GetProperty("merged_at").ValueKind != JsonValueKind.Null
        && pull.GetProperty("base").GetProperty("ref").GetString() == "master"
        && pull.GetProperty("head").GetProperty("ref").GetString()?.StartsWith("release/", StringComparison.Ordinal) == true).ToArray();
    if (candidates.Length == 0)
    {
        return;
    }

    if (candidates.Length != 1)
    {
        throw new InvalidOperationException("Merge commit is associated with multiple release pull requests.");
    }

    var pull = candidates[0];
    var expectedBody = Changelog.Render(current).Trim();
    if (pull.GetProperty("title").GetString()?.Trim() != current.Version
        || pull.GetProperty("head").GetProperty("ref").GetString() != $"release/{current.Version}"
        || NormalizeBody(pull.GetProperty("body").GetString() ?? string.Empty) != expectedBody)
    {
        throw new InvalidOperationException("Release pull request does not match the current changelog entry.");
    }

    var tag = $"v{current.Version}";
    var title = $"{current.Version} — {current.ResolvedCodename}";
    Run("gh", ["auth", "setup-git"], backend);
    Run("git", ["fetch", "--tags", "origin"], backend);
    if (HasRef(backend, $"refs/tags/{tag}"))
    {
        if (Output("git", ["rev-list", "-n", "1", tag], backend) != commit)
        {
            throw new InvalidOperationException($"{tag} already points to a different commit.");
        }
    }
    else
    {
        Run("git", ["tag", "--annotate", tag, commit, "--message", title], backend);
        Run("git", ["push", "origin", tag], backend);
    }

    if (TryOutput("gh", ["release", "view", tag, "--json", "tagName,name,body"], backend, out var releaseJson))
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
        Run("gh", ["release", "create", tag, "--verify-tag", "--title", title, "--notes-file", notesFile], backend);
    }
    finally
    {
        File.Delete(notesFile);
    }
}

static string FindBackendRoot()
{
    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "MelodyTrack.Backend", "changelog.json")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Run this application from the backend repository.");
}

static string NormalizeBody(string value) => value.Trim().TrimStart('\uFEFF');

static void EnsureCleanSource(Repository repository)
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
    if (branch is "" or "master" || branch.StartsWith("release/", StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{repository.Name}: run from a non-master source branch.");
    }
}

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

static bool HasRef(string workingDirectory, string reference) =>
    RunForExitCode("git", ["show-ref", "--verify", "--quiet", reference], workingDirectory) == 0;

static bool HasRemoteRef(string workingDirectory, string branch) =>
    RunForExitCode("git", ["ls-remote", "--exit-code", "--heads", "origin", branch], workingDirectory) == 0;

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
    private static readonly Regex VersionPattern = new(@"^\d{4}\.(0[1-9]|1[0-2])\.[1-9]\d*(?:\.[1-9]\d*)?$", RegexOptions.CultureInvariant);
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
    public ReleaseEntry Current => Releases[0];

    public static Changelog Load(string path)
    {
        var root = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? throw new InvalidDataException("Changelog must be an object.");
        RequireKeys(root, ["releases"], "changelog");
        var array = root["releases"]?.AsArray() ?? throw new InvalidDataException("changelog.releases is required.");
        if (array.Count == 0) throw new InvalidDataException("changelog.releases must not be empty.");
        var releases = array.Select((node, index) => Parse(node?.AsObject(), index)).ToArray();
        if (releases.Select(entry => entry.Version).Distinct(StringComparer.Ordinal).Count() != releases.Length) throw new InvalidDataException("Release versions must be unique.");
        for (var index = 1; index < releases.Length; index++)
        {
            if (releases[index].Date > releases[index - 1].Date) throw new InvalidDataException("Releases must be newest first.");
            if (releases[index].Date == releases[index - 1].Date && Compare(releases[index - 1].Version, releases[index].Version) <= 0) throw new InvalidDataException("Same-day releases must use descending versions.");
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
        var builder = new StringBuilder().Append("# ").Append(entry.Version).Append(" — ").AppendLine(entry.ResolvedCodename).AppendLine().Append("Дата: ").AppendLine(entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        foreach (var category in Categories.Where(category => entry.Changes[category].Count > 0))
        {
            builder.AppendLine().Append("## ").AppendLine(Headings[category]).AppendLine();
            foreach (var change in entry.Changes[category]) builder.Append("- ").AppendLine(change);
        }

        return builder.ToString();
    }

    private static ReleaseEntry Parse(JsonObject? value, int index)
    {
        var entry = value ?? throw new InvalidDataException($"releases[{index}] must be an object.");
        RequireKeys(entry, ["version", "codename", "date", "changes"], $"releases[{index}]", optionalCodename: true);
        var version = RequiredString(entry, "version");
        if (!VersionPattern.IsMatch(version)) throw new InvalidDataException($"releases[{index}].version is invalid.");
        var isPatch = version.Count(character => character == '.') == 3;
        var hasCodename = entry.ContainsKey("codename");
        var codename = hasCodename ? RequiredString(entry, "codename") : null;
        if (isPatch && hasCodename) throw new InvalidDataException("Patches must omit codename.");
        if (!isPatch && codename is null) throw new InvalidDataException("Actual releases require codename.");
        if (!DateOnly.TryParseExact(RequiredString(entry, "date"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)) throw new InvalidDataException("Release date is invalid.");
        var changesObject = entry["changes"]?.AsObject() ?? throw new InvalidDataException("changes is required.");
        RequireKeys(changesObject, Categories, "changes");
        var changes = Categories.ToDictionary(category => category, category => changesObject[category]?.AsArray().Select(item => item?.GetValue<string>()?.Trim() ?? string.Empty).ToArray() as IReadOnlyList<string> ?? []);
        if (changes.Values.SelectMany(items => items).Any(string.IsNullOrWhiteSpace) || changes.Values.Sum(items => items.Count) == 0) throw new InvalidDataException("Changes must contain non-empty text.");
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

sealed class ReleaseEntry(string version, string? codename, DateOnly date, IReadOnlyDictionary<string, IReadOnlyList<string>> changes, string? parentVersion)
{
    public string Version { get; } = version;
    public string? Codename { get; } = codename;
    public DateOnly Date { get; } = date;
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Changes { get; } = changes;
    public string? ParentVersion { get; } = parentVersion;
    public string ResolvedCodename { get; set; } = string.Empty;
}

sealed class Repository(string name, string path, string verifyCommand, string[] verifyArguments)
{
    public string Name { get; } = name;
    public string Path { get; } = path;
    public string VerifyCommand { get; } = verifyCommand;
    public string[] VerifyArguments { get; } = verifyArguments;
    public string? OriginalBranch { get; set; }
    public string? SourceCommit { get; set; }
    public bool RemoteExists { get; set; }
    public PullRequest? PullRequest { get; set; }
}

sealed record PullRequest(string Url, string Title, string Body);
sealed record ProcessResult(int ExitCode, string Output);
