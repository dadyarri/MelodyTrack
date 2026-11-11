using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MelodyTrack.Rewrite.Api;

/// <summary>Loads the application's own release metadata, independently of host paths and database state.</summary>
public sealed partial class ReleaseChangelog
{
    private const string ResourcePrefix = "MelodyTrack.Rewrite.Api.Changelog.";
    private readonly IReadOnlyList<ReleaseResponse> releases;

    private ReleaseChangelog(CurrentReleaseResponse current, IReadOnlyList<ReleaseResponse> releases, string etag)
    {
        Current = current;
        this.releases = releases;
        Etag = etag;
    }

    public CurrentReleaseResponse Current { get; }
    public string Etag { get; }

    public static ReleaseChangelog LoadEmbedded()
    {
        var assembly = typeof(ReleaseChangelog).Assembly;
        var documents = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidDataException("A release resource is missing.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            documents.Add(name[ResourcePrefix.Length..], reader.ReadToEnd());
        }
        if (!documents.Remove("current.json", out var manifest)) throw new InvalidDataException("The current release manifest is missing.");
        return Load(manifest, documents);
    }

    public static ReleaseChangelog Load(string manifest, IReadOnlyDictionary<string, string> documents)
    {
        using var manifestJson = JsonDocument.Parse(manifest);
        CheckProperties(manifestJson.RootElement, ["currentVersion"], ["currentVersion"]);
        var currentVersion = RequiredText(manifestJson.RootElement, "currentVersion");
        CheckVersion(currentVersion);
        var parsed = new Dictionary<string, ReleaseDocument>(StringComparer.Ordinal);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        AppendHash(hash, "current.json", manifest);
        foreach (var (filename, json) in documents.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            using var document = JsonDocument.Parse(json);
            var release = ParseRelease(document.RootElement);
            if (!string.Equals(filename, $"{release.Version}.json", StringComparison.Ordinal)
                || !parsed.TryAdd(release.Version, release)) throw new InvalidDataException("Release filenames and versions must be unique and match.");
            AppendHash(hash, filename, json);
        }
        if (!parsed.TryGetValue(currentVersion, out var current)) throw new InvalidDataException("The current release is not present in the changelog.");
        var drafts = parsed.Values.Where(release => release.Date is null).ToArray();
        if (drafts.Length > 1) throw new InvalidDataException("Only one active release draft is allowed.");
        if (parsed.Values.Any(release => release.Date is not null && CompareVersions(release.Version, currentVersion) > 0))
            throw new InvalidDataException("Published release versions cannot be newer than the current version.");
        foreach (var release in parsed.Values.Where(release => release.ParentVersion is not null))
        {
            if (!parsed.TryGetValue(release.ParentVersion!, out var parent) || parent.ParentVersion is not null || parent.Date is null)
                throw new InvalidDataException("A patch must reference a published regular release.");
        }
        var published = parsed.Values.Where(release => release.Date is not null)
            .OrderByDescending(release => release.Date)
            .ThenByDescending(release => release.Version, Comparer<string>.Create(CompareVersions))
            .ToArray();
        if (drafts.SingleOrDefault() is { } draft && published.Length > 0
            && (CompareVersions(draft.Version, published[0].Version) <= 0
                || draft.ParentVersion is not null && draft.ParentVersion != (published[0].ParentVersion ?? published[0].Version)))
            throw new InvalidDataException("The active draft must follow the current published release.");
        var currentNamed = current.ParentVersion is { } currentParent ? parsed[currentParent] : current;
        var visible = parsed.Values.Where(release => release.Date is not null || release.Version == currentVersion)
            .OrderByDescending(release => release.Version == currentVersion)
            .ThenByDescending(release => release.Date)
            .ThenByDescending(release => release.Version, Comparer<string>.Create(CompareVersions))
            .Select(release =>
            {
                var named = release.ParentVersion is { } parentVersion ? parsed[parentVersion] : release;
                return new ReleaseResponse(release.Version, named.Codename, named.CodenameMeaning, release.Date,
                    release.Changes, release.ParentVersion);
            }).ToArray();
        return new(new(current.Version, currentNamed.Codename, currentNamed.CodenameMeaning), visible,
            $"\"{Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant()}\"");
    }

    public ReleasesResponse GetPage(int page, int pageSize)
    {
        if (page < 1 || pageSize is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(page));
        var skip = (long)(page - 1) * pageSize;
        ReleaseResponse[] entries = skip >= releases.Count ? [] : releases.Skip((int)skip).Take(pageSize).ToArray();
        return new(Current.Version, entries, page, pageSize, releases.Count,
            (int)Math.Ceiling(releases.Count / (double)pageSize), (long)page * pageSize < releases.Count);
    }

    private static ReleaseDocument ParseRelease(JsonElement value)
    {
        CheckProperties(value, ["version", "codename", "codenameMeaning", "date", "changes"], ["version", "date", "changes"]);
        var version = RequiredText(value, "version");
        CheckVersion(version);
        DateOnly? date = null;
        var dateValue = value.GetProperty("date");
        if (dateValue.ValueKind == JsonValueKind.String && DateOnly.TryParseExact(dateValue.GetString(), "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate)) date = parsedDate;
        else if (dateValue.ValueKind != JsonValueKind.Null) throw new InvalidDataException("Release dates use yyyy-MM-dd or null for a draft.");
        var codename = OptionalText(value, "codename");
        var meaning = OptionalText(value, "codenameMeaning");
        var parent = version.Count(character => character == '.') == 3 ? version[..version.LastIndexOf('.')] : null;
        if (parent is not null && (value.TryGetProperty("codename", out _) || value.TryGetProperty("codenameMeaning", out _)))
            throw new InvalidDataException("A patch inherits its codename from its regular release.");
        if (date is not null && parent is null && (codename is null || meaning is null))
            throw new InvalidDataException("A published regular release requires its codename and meaning.");
        var changes = value.GetProperty("changes");
        CheckProperties(changes, ["new", "improved", "fixed", "security"], ["new", "improved", "fixed", "security"]);
        var parsedChanges = new ReleaseChanges(TextArray(changes, "new"), TextArray(changes, "improved"),
            TextArray(changes, "fixed"), TextArray(changes, "security"));
        if (date is not null && parsedChanges.New.Count + parsedChanges.Improved.Count + parsedChanges.Fixed.Count + parsedChanges.Security.Count == 0)
            throw new InvalidDataException("A published release requires at least one change.");
        return new(version, codename, meaning, date, parsedChanges, parent);
    }

    private static void CheckProperties(JsonElement value, string[] allowed, string[] required)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Release metadata must be an object.");
        var properties = value.EnumerateObject().Select(property => property.Name).ToArray();
        if (properties.Distinct(StringComparer.Ordinal).Count() != properties.Length
            || properties.Except(allowed, StringComparer.Ordinal).Any()
            || required.Except(properties, StringComparer.Ordinal).Any()) throw new InvalidDataException("Release metadata contains missing, duplicate or unknown properties.");
    }

    private static string RequiredText(JsonElement value, string property) => OptionalText(value, property)
        ?? throw new InvalidDataException("A required release metadata string is missing.");

    private static string? OptionalText(JsonElement value, string property)
    {
        if (!value.TryGetProperty(property, out var text) || text.ValueKind == JsonValueKind.Null) return null;
        if (text.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(text.GetString()))
            throw new InvalidDataException("Release metadata strings must be nonempty.");
        return text.GetString()!.Trim();
    }

    private static IReadOnlyList<string> TextArray(JsonElement value, string property)
    {
        var array = value.GetProperty(property);
        if (array.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Release change groups must be arrays.");
        return array.EnumerateArray().Select(item => item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())
            ? item.GetString()!.Trim() : throw new InvalidDataException("Release changes must be nonempty strings.")).ToArray();
    }

    private static void CheckVersion(string version)
    {
        if (!VersionPattern().IsMatch(version) || version.Split('.').Any(part => !int.TryParse(part, CultureInfo.InvariantCulture, out _)))
            throw new InvalidDataException("Release versions use YYYY.MM.N or YYYY.MM.N.P.");
    }

    private static int CompareVersions(string left, string right)
    {
        var first = left.Split('.').Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        var second = right.Split('.').Select(part => int.Parse(part, CultureInfo.InvariantCulture)).ToArray();
        for (var index = 0; index < Math.Max(first.Length, second.Length); index++)
        {
            var compared = (index < first.Length ? first[index] : 0).CompareTo(index < second.Length ? second[index] : 0);
            if (compared != 0) return compared;
        }
        return 0;
    }

    private static void AppendHash(IncrementalHash hash, string name, string value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(name));
        hash.AppendData([0]);
        hash.AppendData(Encoding.UTF8.GetBytes(value));
        hash.AppendData([0]);
    }

    [GeneratedRegex(@"^\d{4}\.(?:0[1-9]|1[0-2])\.[1-9]\d*(?:\.[1-9]\d*)?$")]
    private static partial Regex VersionPattern();

    private sealed record ReleaseDocument(string Version, string? Codename, string? CodenameMeaning, DateOnly? Date,
        ReleaseChanges Changes, string? ParentVersion);
}
