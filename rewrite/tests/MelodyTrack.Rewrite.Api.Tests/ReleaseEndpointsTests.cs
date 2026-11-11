using System.Net;
using System.Net.Http.Json;
using MelodyTrack.Rewrite.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class ReleaseEndpointsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task GetCurrent_AnonymousRequest_ReturnsBackendVersionAndCacheHeaders()
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync("/api/releases/current", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var current = await response.Content.ReadFromJsonAsync<CurrentReleaseResponse>(Token);
        Assert.Equal("2026.12.1", current!.Version);
        Assert.NotNull(response.Headers.ETag);
        Assert.True(response.Headers.CacheControl?.Public);
    }

    [Fact]
    public async Task GetHistory_MatchingEtag_ReturnsNotModifiedWithoutBody()
    {
        await using var host = await StartAsync();
        using var first = await host.Client.GetAsync("/api/releases?page=1&page_size=1", Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/releases?page=1&page_size=1");
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        using var response = await host.Client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.NotModified, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsByteArrayAsync(Token));
        Assert.Equal(first.Headers.ETag, response.Headers.ETag);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("page_size=0")]
    [InlineData("page_size=51")]
    public async Task GetHistory_InvalidPagination_ReturnsValidationProblemWithoutCaching(string query)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync($"/api/releases?{query}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task GetHistory_UnpublishedCurrent_ReturnsSelectedVersionWithoutInventedDate()
    {
        await using var host = await StartAsync();

        var history = await host.Client.GetFromJsonAsync<ReleasesResponse>("/api/releases", Token);

        Assert.NotNull(history);
        Assert.Equal("2026.12.1", history.CurrentVersion);
        Assert.Null(Assert.Single(history.Releases).Date);
        Assert.Equal(1, history.Page);
        Assert.Equal(2, history.PageSize);
        Assert.Equal(1, history.TotalPages);
    }

    private static async Task<Host> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddReleaseChangelog();
        builder.Services.Replace(ServiceDescriptor.Singleton(ReleaseChangelogTests.Load("2026.12.1",
            ("2026.12.1", ReleaseChangelogTests.Entry("2026.12.1", null)))));
        var app = builder.Build();
        app.UseRateLimiter();
        app.MapReleaseEndpoints();
        await app.StartAsync(Token);
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new(app, new HttpClient { BaseAddress = new(address) });
    }

    private sealed class Host(WebApplication app, HttpClient client) : IAsyncDisposable
    {
        public HttpClient Client { get; } = client;
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
        }
    }
}
