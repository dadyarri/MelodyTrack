using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MelodyTrack.Backend.Services;

public sealed partial class ReleaseChangelog
{
    private static readonly string[] AllowedReleaseProperties = ["version", "codename", "date", "changes"];
    private static readonly string[] AllowedChangeProperties = ["new", "improved", "fixed", "security"];

    private ReleaseChangelog(IReadOnlyList<ReleaseEntry> releases, string etag)
    {
        Releases = releases;
        Etag = etag;
    }

    public IReadOnlyList<ReleaseEntry> Releases { get; }
    public ReleaseEntry Current => Releases[0];
    public string Etag { get; }

    public static ReleaseChangelog Load(string releasesDirectory)
    {
        if (!Directory.Exists(releasesDirectory))
        {
            throw new DirectoryNotFoundException($"Release changelog directory was not found: {releasesDirectory}");
        }

        var paths = Directory.EnumerateFiles(releasesDirectory, "*.json", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        if (paths.Length == 0)
        {
            throw new InvalidDataException("The release changelog must contain at least one JSON file.");
        }

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var releases = new List<ReleaseEntry>();
        var versions = new HashSet<string>(StringComparer.Ordinal);
        var drafts = new List<ParsedRelease>();
        foreach (var path in paths)
        {
            var filename = Path.GetFileName(path);
            var bytes = File.ReadAllBytes(path);
            var jsonOffset = bytes.Length >= 3
                && bytes[0] == 0xEF
                && bytes[1] == 0xBB
                && bytes[2] == 0xBF
                    ? 3
                    : 0;
            using var document = JsonDocument.Parse(bytes.AsMemory(jsonOffset), new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow
            });
            var parsed = ParseRelease(document.RootElement, filename);
            if (!versions.Add(parsed.Version))
            {
                throw new InvalidDataException($"{filename}.version is duplicated.");
            }

            if (!string.Equals(Path.GetFileNameWithoutExtension(path), parsed.Version, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"{filename} must match release version {parsed.Version}.");
            }

            if (parsed.IsDraft)
            {
                drafts.Add(parsed);
                continue;
            }

            hash.AppendData(Encoding.UTF8.GetBytes(filename));
            hash.AppendData([0]);
            hash.AppendData(bytes);
            releases.Add(parsed.Release!);
        }

        if (drafts.Count > 1)
        {
            throw new InvalidDataException("The changelog may contain only one active draft.");
        }
        if (releases.Count == 0)
        {
            throw new InvalidDataException("The changelog must contain at least one released entry.");
        }

        releases.Sort((left, right) =>
        {
            var dateComparison = right.Date.CompareTo(left.Date);
            if (dateComparison != 0)
            {
                return dateComparison;
            }

            return CompareVersions(right.Version, left.Version);
        });

        if (drafts.SingleOrDefault() is { } activeDraft)
        {
            var productionParent = releases[0].ParentVersion ?? releases[0].Version;
            if (activeDraft.ParentVersion is not null && activeDraft.ParentVersion != productionParent)
            {
                throw new InvalidDataException($"Draft patch {activeDraft.Version} must target current production parent {productionParent}.");
            }

            if (CompareVersions(activeDraft.Version, releases[0].Version) <= 0)
            {
                throw new InvalidDataException($"Draft {activeDraft.Version} must be newer than current production {releases[0].Version}.");
            }
        }

        var byVersion = releases.ToDictionary(release => release.Version, StringComparer.Ordinal);
        foreach (var release in releases.Where(release => release.IsPatch))
        {
            if (!byVersion.TryGetValue(release.ParentVersion!, out var parent) || parent.IsPatch)
            {
                throw new InvalidDataException($"Patch {release.Version} must reference an actual release in the same changelog.");
            }

            release.ResolvedCodename = parent.Codename!;
        }
        foreach (var draft in drafts.Where(draft => draft.ParentVersion is not null))
        {
            if (!byVersion.TryGetValue(draft.ParentVersion!, out var parent) || parent.IsPatch)
            {
                throw new InvalidDataException($"Draft patch {draft.Version} must reference a released actual release in the same changelog.");
            }
        }

        var etag = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        return new ReleaseChangelog(releases, $"\"{etag}\"");
    }

    private static int CompareVersions(string left, string right)
    {
        var leftParts = left.Split('.').Select(int.Parse).ToArray();
        var rightParts = right.Split('.').Select(int.Parse).ToArray();
        for (var index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            var comparison = (index < leftParts.Length ? leftParts[index] : 0)
                .CompareTo(index < rightParts.Length ? rightParts[index] : 0);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        return 0;
    }

    private static ParsedRelease ParseRelease(JsonElement element, string location)
    {
        var releaseObject = RequireObject(element, location);
        RequireExactProperties(releaseObject, AllowedReleaseProperties, location, allowMissingCodename: true);
        var version = RequireNonEmptyString(releaseObject, "version", location);
        var versionMatch = VersionPattern().Match(version);
        if (!versionMatch.Success)
        {
            throw new InvalidDataException($"{location}.version must use yyyy.mm.release or yyyy.mm.release.patch format.");
        }

        var dateElement = RequireProperty(releaseObject, "date");
        DateOnly? date = null;
        if (dateElement.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(
                dateElement.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
        {
            date = parsedDate;
        }
        else if (dateElement.ValueKind != JsonValueKind.Null)
        {
            throw new InvalidDataException($"{location}.date must use yyyy-MM-dd format or be null for the active draft.");
        }

        var isPatch = versionMatch.Groups["patch"].Success;
        var hasCodename = releaseObject.TryGetProperty("codename", out var codenameElement);
        string? codename = null;
        if (hasCodename)
        {
            codename = codenameElement.ValueKind == JsonValueKind.String ? codenameElement.GetString()?.Trim() : null;
        }

        if (isPatch && hasCodename)
        {
            throw new InvalidDataException($"{location}.codename must be omitted for patches.");
        }

        if (!isPatch && string.IsNullOrWhiteSpace(codename))
        {
            throw new InvalidDataException($"{location}.codename is required for actual releases.");
        }

        var changes = ParseChanges(RequireProperty(releaseObject, "changes"), $"{location}.changes");
        if (date is not null && changes.New.Count + changes.Improved.Count + changes.Fixed.Count + changes.Security.Count == 0)
        {
            throw new InvalidDataException($"{location}.changes must contain at least one item.");
        }

        var parentVersion = isPatch ? version[..version.LastIndexOf('.')] : null;
        return date is null
            ? new ParsedRelease(version, parentVersion, IsDraft: true, Release: null)
            : new ParsedRelease(
                version,
                parentVersion,
                IsDraft: false,
                new ReleaseEntry(version, codename, date.Value, changes, parentVersion) { ResolvedCodename = codename ?? string.Empty });
    }

    private static ReleaseChanges ParseChanges(JsonElement element, string location)
    {
        var changes = RequireObject(element, location);
        RequireExactProperties(changes, AllowedChangeProperties, location);
        return new ReleaseChanges(
            ParseStringArray(changes, "new", location),
            ParseStringArray(changes, "improved", location),
            ParseStringArray(changes, "fixed", location),
            ParseStringArray(changes, "security", location));
    }

    private static IReadOnlyList<string> ParseStringArray(JsonElement parent, string name, string location)
    {
        var element = RequireProperty(parent, name);
        if (element.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException($"{location}.{name} must be an array.");
        }

        var values = element.EnumerateArray().Select((value, index) =>
        {
            if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            {
                throw new InvalidDataException($"{location}.{name}[{index}] must be a non-empty string.");
            }

            return value.GetString()!.Trim();
        }).ToArray();
        return values;
    }

    private static JsonElement RequireObject(JsonElement element, string location)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{location} must be an object.");
        }

        return element;
    }

    private static JsonElement RequireProperty(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw new InvalidDataException($"Missing required property '{name}'.");
        }

        return value;
    }

    private static string RequireNonEmptyString(JsonElement element, string name, string location)
    {
        var value = RequireProperty(element, name);
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"{location}.{name} must be a non-empty string.");
        }

        return value.GetString()!.Trim();
    }

    private static void RequireExactProperties(
        JsonElement element,
        IReadOnlyCollection<string> allowed,
        string location,
        bool allowMissingCodename = false)
    {
        var properties = element.EnumerateObject().Select(property => property.Name).ToArray();
        var unknown = properties.Except(allowed, StringComparer.Ordinal).FirstOrDefault();
        if (unknown is not null)
        {
            throw new InvalidDataException($"Unknown property {location}.{unknown}.");
        }

        foreach (var required in allowed.Where(name => !allowMissingCodename || name != "codename"))
        {
            if (!properties.Contains(required, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"Missing required property {location}.{required}.");
            }
        }
    }

    [GeneratedRegex(@"^(?<year>\d{4})\.(?<month>0[1-9]|1[0-2])\.(?<release>[1-9]\d*)(?:\.(?<patch>[1-9]\d*))?$")]
    private static partial Regex VersionPattern();

    private sealed record ParsedRelease(string Version, string? ParentVersion, bool IsDraft, ReleaseEntry? Release);
}

public sealed class ReleaseEntry(
    string version,
    string? codename,
    DateOnly date,
    ReleaseChanges changes,
    string? parentVersion)
{
    public string Version { get; } = version;
    public string? Codename { get; } = codename;
    public DateOnly Date { get; } = date;
    public ReleaseChanges Changes { get; } = changes;
    public string? ParentVersion { get; } = parentVersion;
    public bool IsPatch => ParentVersion is not null;
    public string ResolvedCodename { get; internal set; } = null!;
}

public sealed record ReleaseChanges(
    IReadOnlyList<string> New,
    IReadOnlyList<string> Improved,
    IReadOnlyList<string> Fixed,
    IReadOnlyList<string> Security);
