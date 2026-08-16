#!/usr/bin/env dotnet
#:property TargetFramework=net10.0

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var command = args.FirstOrDefault() ?? "help";
if (command == "help")
{
    Console.WriteLine("Usage: dotnet run scripts/ReleaseWorkflow.cs -- current-version|publish");
    return;
}

var repository = Required("GITHUB_REPOSITORY");
var commit = Required("GITHUB_SHA");
var requestedVersion = Optional("RELEASE_VERSION");
var release = requestedVersion is null
    ? FindReleasePullRequestForCommit(repository, commit)
    : FindReleasePullRequestForVersion(repository, commit, requestedVersion);

if (command == "current-version")
{
    Console.Write(release?.Version ?? "dev");
    return;
}

if (command != "publish")
{
    Console.WriteLine("Usage: dotnet run scripts/ReleaseWorkflow.cs -- current-version|publish");
    return;
}

if (release is null)
{
    return;
}

Publish(release, commit);

static ReleasePullRequest? FindReleasePullRequestForCommit(string repository, string commit)
{
    using var document = JsonDocument.Parse(Output("gh", ["api", $"repos/{repository}/commits/{commit}/pulls"]));
    var pulls = document.RootElement.EnumerateArray().Where(pull =>
        pull.GetProperty("merged_at").ValueKind != JsonValueKind.Null
        && pull.GetProperty("base").GetProperty("ref").GetString() == "master"
        && pull.GetProperty("head").GetProperty("ref").GetString()?.StartsWith("release/", StringComparison.Ordinal) == true).ToArray();
    if (pulls.Length == 0) return null;
    if (pulls.Length != 1) throw new InvalidOperationException("Merge commit is associated with multiple release pull requests.");

    var pull = pulls[0];
    return ParseReleasePullRequest(
        pull.GetProperty("title").GetString(),
        pull.GetProperty("body").GetString(),
        pull.GetProperty("head").GetProperty("ref").GetString());
}

static ReleasePullRequest FindReleasePullRequestForVersion(string repository, string commit, string requestedVersion)
{
    if (!IsValidVersion(requestedVersion))
    {
        throw new InvalidOperationException("RELEASE_VERSION is invalid.");
    }

    using var document = JsonDocument.Parse(Output(
        "gh",
        [
            "pr", "list",
            "--repo", repository,
            "--state", "merged",
            "--base", "master",
            "--head", $"release/{requestedVersion}",
            "--limit", "2",
            "--json", "title,body,headRefName,baseRefName,mergedAt,mergeCommit"
        ]));
    var pulls = document.RootElement.EnumerateArray().Where(pull =>
        pull.GetProperty("mergedAt").ValueKind != JsonValueKind.Null
        && pull.GetProperty("baseRefName").GetString() == "master"
        && pull.GetProperty("headRefName").GetString() == $"release/{requestedVersion}").ToArray();
    if (pulls.Length == 0)
    {
        throw new InvalidOperationException($"No merged release/{requestedVersion} pull request was found.");
    }

    if (pulls.Length != 1)
    {
        throw new InvalidOperationException($"Multiple merged release/{requestedVersion} pull requests were found.");
    }

    var pull = pulls[0];
    var release = ParseReleasePullRequest(
        pull.GetProperty("title").GetString(),
        pull.GetProperty("body").GetString(),
        pull.GetProperty("headRefName").GetString());
    if (release.Version != requestedVersion)
    {
        throw new InvalidOperationException("Merged release pull request does not match RELEASE_VERSION.");
    }

    var mergeCommit = pull.GetProperty("mergeCommit").GetProperty("oid").GetString()
        ?? throw new InvalidOperationException("Merged release pull request has no merge commit.");
    if (!Success("git", ["merge-base", "--is-ancestor", mergeCommit, commit]))
    {
        throw new InvalidOperationException($"release/{requestedVersion} is not contained in the current master commit.");
    }

    return release;
}

static ReleasePullRequest ParseReleasePullRequest(string? title, string? pullBody, string? branch)
{
    var version = title?.Trim() ?? string.Empty;
    var body = NormalizeBody(pullBody ?? string.Empty);
    if (!IsValidVersion(version) || branch != $"release/{version}")
    {
        throw new InvalidOperationException("Release pull request title or branch is invalid.");
    }

    var prefix = $"# {version} — ";
    var heading = body.Split('\n', 2)[0];
    if (!heading.StartsWith(prefix, StringComparison.Ordinal) || string.IsNullOrWhiteSpace(heading[prefix.Length..]))
    {
        throw new InvalidOperationException("Release pull request body has no resolved codename heading.");
    }

    return new ReleasePullRequest(version, heading[prefix.Length..].Trim(), body);
}

static bool IsValidVersion(string version) =>
    Regex.IsMatch(version, @"^\d{4}\.(0[1-9]|1[0-2])\.[1-9]\d*(?:\.[1-9]\d*)?$");

static void Publish(ReleasePullRequest release, string commit)
{
    var tag = $"v{release.Version}";
    var title = $"{release.Version} — {release.Codename}";
    Run("gh", ["auth", "setup-git"]);
    Run("git", ["fetch", "--tags", "origin"]);
    if (Success("git", ["show-ref", "--verify", "--quiet", $"refs/tags/{tag}"]))
    {
        if (Output("git", ["rev-list", "-n", "1", tag]) != commit)
        {
            throw new InvalidOperationException($"{tag} already points to a different commit.");
        }
    }
    else
    {
        Run("git", [
            "-c", "user.name=github-actions[bot]",
            "-c", "user.email=41898282+github-actions[bot]@users.noreply.github.com",
            "tag", "--annotate", tag, commit, "--message", title
        ]);
        Run("git", ["push", "origin", tag]);
    }

    if (TryOutput("gh", ["release", "view", tag, "--json", "tagName,name,body"], out var existingJson))
    {
        using var existing = JsonDocument.Parse(existingJson);
        var root = existing.RootElement;
        if (root.GetProperty("tagName").GetString() != tag
            || root.GetProperty("name").GetString() != title
            || NormalizeBody(root.GetProperty("body").GetString() ?? string.Empty) != release.Body)
        {
            throw new InvalidOperationException($"{tag} GitHub Release metadata conflicts with the pull request.");
        }

        return;
    }

    var notes = Path.Combine(Path.GetTempPath(), $"melodytrack-release-{Guid.NewGuid():N}.md");
    File.WriteAllText(notes, $"{release.Body}\n", new UTF8Encoding(false));
    try
    {
        Run("gh", ["release", "create", tag, "--verify-tag", "--title", title, "--notes-file", notes]);
    }
    finally
    {
        File.Delete(notes);
    }
}

static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
    ? value
    : throw new InvalidOperationException($"{name} is required.");

static string? Optional(string name) => Environment.GetEnvironmentVariable(name)?.Trim() is { Length: > 0 } value
    ? value
    : null;

static string NormalizeBody(string value) => value.Trim().TrimStart('\uFEFF');

static bool Success(string command, IReadOnlyList<string> arguments) => Execute(command, arguments, false).ExitCode == 0;
static void Run(string command, IReadOnlyList<string> arguments)
{
    if (!Success(command, arguments)) throw new InvalidOperationException($"{command} failed.");
}

static string Output(string command, IReadOnlyList<string> arguments)
{
    var result = Execute(command, arguments, true);
    if (result.ExitCode != 0) throw new InvalidOperationException($"{command} failed.");
    return result.Output.Trim();
}

static bool TryOutput(string command, IReadOnlyList<string> arguments, out string output)
{
    var result = Execute(command, arguments, true);
    output = result.Output.Trim();
    return result.ExitCode == 0;
}

static ProcessResult Execute(string command, IReadOnlyList<string> arguments, bool capture)
{
    var startInfo = new ProcessStartInfo(command) { UseShellExecute = false, RedirectStandardOutput = capture };
    foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
    using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"Could not start {command}.");
    var output = capture ? process.StandardOutput.ReadToEnd() : string.Empty;
    process.WaitForExit();
    return new ProcessResult(process.ExitCode, output);
}

sealed record ReleasePullRequest(string Version, string Codename, string Body);
sealed record ProcessResult(int ExitCode, string Output);
