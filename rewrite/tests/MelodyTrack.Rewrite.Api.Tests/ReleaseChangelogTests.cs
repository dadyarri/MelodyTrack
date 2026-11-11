using System.Text.Json;
using MelodyTrack.Rewrite.Api;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class ReleaseChangelogTests
{
    [Fact]
    public void Load_UnpublishedCurrentVersion_ExposesSelectedVersionWithoutPublicationDate()
    {
        var changelog = Load("2026.12.1", ("2026.12.1", Entry("2026.12.1", null)));

        var history = changelog.GetPage(1, 2);

        Assert.Equal("2026.12.1", changelog.Current.Version);
        Assert.Null(changelog.Current.Codename);
        Assert.Null(Assert.Single(history.Releases).Date);
        Assert.Equal(1, history.TotalCount);
        Assert.Equal(1, history.TotalPages);
        Assert.False(history.HasNextPage);
    }

    [Fact]
    public void Load_PublishedPatch_InheritsRegularCodenameAndUsesNumericVersionOrder()
    {
        var date = new DateOnly(2026, 11, 1);
        var changelog = Load("2026.11.1.10",
            ("2026.11.1", Entry("2026.11.1", date)),
            ("2026.11.1.2", Entry("2026.11.1.2", date)),
            ("2026.11.1.10", Entry("2026.11.1.10", date)));

        var first = changelog.GetPage(1, 2);
        var second = changelog.GetPage(2, 2);

        Assert.Equal(["2026.11.1.10", "2026.11.1.2"], first.Releases.Select(release => release.Version));
        Assert.Equal("Allegro", changelog.Current.Codename);
        Assert.All(first.Releases, release => Assert.Equal("Allegro", release.Codename));
        Assert.True(first.HasNextPage);
        Assert.Equal(3, first.TotalCount);
        Assert.Equal(2, first.TotalPages);
        Assert.Equal("2026.11.1", Assert.Single(second.Releases).Version);
        Assert.False(second.HasNextPage);
    }

    [Fact]
    public void GetPage_HugePage_DoesNotOverflowOrRepeatHistory()
    {
        var changelog = Load("2026.11.1", ("2026.11.1", Entry("2026.11.1", new DateOnly(2026, 11, 1))));

        var page = changelog.GetPage(int.MaxValue, 50);

        Assert.Empty(page.Releases);
        Assert.False(page.HasNextPage);
    }

    [Fact]
    public void Load_FutureUnselectedDraft_LeavesItOutOfHistory()
    {
        var changelog = Load("2026.11.1", ("2026.11.1", Entry("2026.11.1", new DateOnly(2026, 11, 1))),
            ("2026.12.1", Entry("2026.12.1", null)));

        var history = changelog.GetPage(1, 2);

        Assert.Equal("2026.11.1", Assert.Single(history.Releases).Version);
    }

    [Fact]
    public void Load_MetadataChanges_ChangesEtagAndRemainsDeterministic()
    {
        var first = Load("2026.12.1", ("2026.12.1", Entry("2026.12.1", null)));
        var equal = Load("2026.12.1", ("2026.12.1", Entry("2026.12.1", null)));
        var changed = Load("2026.12.1", ("2026.12.1", Entry("2026.12.1", null, ["Different draft text"])));

        Assert.Equal(first.Etag, equal.Etag);
        Assert.NotEqual(first.Etag, changed.Etag);
    }

    [Fact]
    public void Load_UnknownCurrentVersion_RejectsManifest()
    {
        Assert.Throws<InvalidDataException>(() => Load("2026.12.2", ("2026.12.1", Entry("2026.12.1", null))));
    }

    [Fact]
    public void Load_TwoDrafts_RejectsAmbiguousActiveRelease()
    {
        Assert.Throws<InvalidDataException>(() => Load("2026.12.2",
            ("2026.12.1", Entry("2026.12.1", null)), ("2026.12.2", Entry("2026.12.2", null))));
    }

    [Fact]
    public void Load_PublishedReleaseWithoutChanges_RejectsEntry()
    {
        Assert.Throws<InvalidDataException>(() => Load("2026.11.1",
            ("2026.11.1", Entry("2026.11.1", new DateOnly(2026, 11, 1), []))));
    }

    [Fact]
    public void Load_OrphanPatch_RejectsMissingParent()
    {
        Assert.Throws<InvalidDataException>(() => Load("2026.11.1.1",
            ("2026.11.1.1", Entry("2026.11.1.1", new DateOnly(2026, 11, 1)))));
    }

    [Fact]
    public void LoadEmbedded_ApplicationResources_ExposeValidatedProductVersion()
    {
        var changelog = ReleaseChangelog.LoadEmbedded();

        Assert.Matches(@"^\d{4}\.(0[1-9]|1[0-2])\.[1-9]\d*(\.[1-9]\d*)?$", changelog.Current.Version);
        Assert.StartsWith("\"", changelog.Etag);
    }

    internal static ReleaseChangelog Load(string current, params (string Version, string Json)[] entries) =>
        ReleaseChangelog.Load(JsonSerializer.Serialize(new { currentVersion = current }),
            entries.ToDictionary(entry => $"{entry.Version}.json", entry => entry.Json));

    internal static string Entry(string version, DateOnly? date, string[]? changes = null)
    {
        var value = new Dictionary<string, object?>
        {
            ["version"] = version,
            ["date"] = date
        };
        // Use the public changelog group names, including C# keywords, without a separate model implementation.
        value["changes"] = new Dictionary<string, object?>
        {
            ["new"] = changes ?? (date is null ? [] : ["Synthetic release change"]),
            ["improved"] = Array.Empty<string>(), ["fixed"] = Array.Empty<string>(), ["security"] = Array.Empty<string>()
        };
        if (version.Count(character => character == '.') == 2)
        {
            value["codename"] = date is null ? null : "Allegro";
            value["codenameMeaning"] = date is null ? null : "Быстро";
        }
        return JsonSerializer.Serialize(value);
    }
}
