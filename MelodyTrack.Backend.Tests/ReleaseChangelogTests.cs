using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MelodyTrack.Backend.Api.Releases.Responses;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class ReleaseChangelogTests
{
    [Fact]
    public void Load_ResolvesPatchCodenameFromParentRelease()
    {
        var changelog = Load(
            """
            {
              "version": "2026.07.1.1",
              "date": "2026-07-30",
              "changes": { "new": [], "improved": [], "fixed": ["Исправление"], "security": [] }
            }
            """,
            """
            {
              "version": "2026.07.1",
              "codename": "Accordatura",
              "date": "2026-07-29",
              "changes": { "new": ["Релиз"], "improved": [], "fixed": [], "security": [] }
            }
            """);

        changelog.Current.Version.ShouldBe("2026.07.1.1");
        changelog.Current.Codename.ShouldBeNull();
        changelog.Current.ResolvedCodename.ShouldBe("Accordatura");
        changelog.Current.ParentVersion.ShouldBe("2026.07.1");
    }

    [Theory]
    [InlineData("{\"version\":\"2026.07.1\",\"codename\":\"Accordatura\",\"date\":\"2026-07-29\",\"changes\":{\"new\":[\"x\"],\"improved\":[],\"fixed\":[],\"security\":[]},\"schemaVersion\":1}")]
    [InlineData("{}")]
    [InlineData("{\"version\":\"2026.07.1\",\"date\":\"2026-07-29\",\"changes\":{\"new\":[\"x\"],\"improved\":[],\"fixed\":[],\"security\":[]}}")]
    [InlineData("{\"version\":\"2026.07.1.1\",\"codename\":\"Wrong\",\"date\":\"2026-07-29\",\"changes\":{\"new\":[\"x\"],\"improved\":[],\"fixed\":[],\"security\":[]}}")]
    [InlineData("{\"version\":\"2026.07.1.1\",\"date\":\"2026-07-29\",\"changes\":{\"new\":[\"x\"],\"improved\":[],\"fixed\":[],\"security\":[]}}")]
    public void Load_RejectsInvalidChangelog(string json)
    {
        Should.Throw<InvalidDataException>(() => Load(json));
    }

    private static ReleaseChangelog Load(params string[] releases)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"melodytrack-changelog-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(directory);
            for (var index = 0; index < releases.Length; index++)
            {
                using var document = JsonDocument.Parse(releases[index]);
                var filename = document.RootElement.TryGetProperty("version", out var version)
                    ? version.GetString()
                    : $"invalid-{index}";
                File.WriteAllText(Path.Combine(directory, $"{filename}.json"), releases[index], Encoding.UTF8);
            }

            return ReleaseChangelog.Load(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

[Collection(IntegrationTestCollection.Name)]
public class ReleaseEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task CurrentRelease_IsAnonymousAndCacheable()
    {
        var configuredRelease = App.Services.GetRequiredService<ReleaseChangelog>().Current;
        var response = await App.Client.GetAsync("/releases/current", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Headers.CacheControl?.Public.ShouldBeTrue();
        response.Headers.ETag.ShouldNotBeNull();
        var release = await response.Content.ReadFromJsonAsync<CurrentReleaseResponse>(TestContext.Current.CancellationToken);
        release.ShouldNotBeNull();
        release.Version.ShouldBe(configuredRelease.Version);
        release.Codename.ShouldBe(configuredRelease.ResolvedCodename);

        using var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, "/releases/current");
        conditionalRequest.Headers.IfNoneMatch.Add(response.Headers.ETag);
        var conditionalResponse = await App.Client.SendAsync(conditionalRequest, TestContext.Current.CancellationToken);
        conditionalResponse.StatusCode.ShouldBe(HttpStatusCode.NotModified);
    }

    [Fact]
    public async Task Releases_ReturnsPagedHistory()
    {
        var configuredChangelog = App.Services.GetRequiredService<ReleaseChangelog>();
        var response = await App.Client.GetAsync("/releases?page=1&page_size=1", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var changelog = await response.Content.ReadFromJsonAsync<ReleasesResponse>(TestContext.Current.CancellationToken);
        changelog.ShouldNotBeNull();
        changelog.CurrentVersion.ShouldBe(configuredChangelog.Current.Version);
        changelog.Releases.Count.ShouldBe(1);
        changelog.TotalCount.ShouldBe(configuredChangelog.Releases.Count);
        changelog.TotalPages.ShouldBe(configuredChangelog.Releases.Count);
        changelog.HasNextPage.ShouldBe(configuredChangelog.Releases.Count > 1);
    }

    [Fact]
    public async Task Releases_DefaultsToTwoEntriesPerPage()
    {
        var response = await App.Client.GetAsync("/releases", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var changelog = await response.Content.ReadFromJsonAsync<ReleasesResponse>(TestContext.Current.CancellationToken);
        changelog.ShouldNotBeNull();
        changelog.PageSize.ShouldBe(2);
        changelog.Releases.Count.ShouldBeLessThanOrEqualTo(2);
    }
}

public class StartupBannerTests
{
    [Fact]
    public void Render_IncludesAsciiLogoVersionAndCodename()
    {
        var banner = StartupBanner.Render("2026.07.2", "Aria");

        banner.ShouldContain("__  __");
        banner.ShouldContain("MelodyTrack", Case.Insensitive);
        banner.ShouldContain("MelodyTrack · Version 2026.07.2 · Aria");
    }
}
