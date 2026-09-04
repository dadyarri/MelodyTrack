using System.IO.Compression;
using System.Net;
using MelodyTrack.Backend.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class UnifiedRuntimeHostingTests
{
    [Fact]
    public async Task UnifiedRuntime_ServesSpaApiAssetsCompressionHeadersAndHealth()
    {
        await using var runtime = await UnifiedRuntime.StartAsync(TestContext.Current.CancellationToken);

        var root = await runtime.Client.GetAsync("/", TestContext.Current.CancellationToken);
        await AssertSpaAsync(root);

        var nestedRoute = await runtime.Client.GetAsync("/clients/example/history", TestContext.Current.CancellationToken);
        await AssertSpaAsync(nestedRoute);

        var api = await runtime.Client.GetAsync("/api/ping", TestContext.Current.CancellationToken);
        api.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await api.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldBe("pong");

        var missingApi = await runtime.Client.GetAsync("/api/missing", TestContext.Current.CancellationToken);
        missingApi.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        missingApi.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await missingApi.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldNotContain("<div id=\"root\">");

        var asset = await runtime.Client.GetAsync("/assets/application-a1b2c3.js", TestContext.Current.CancellationToken);
        asset.StatusCode.ShouldBe(HttpStatusCode.OK);
        asset.Headers.CacheControl?.ToString().ShouldBe("public, max-age=31536000, immutable");
        asset.Content.Headers.ContentType?.MediaType.ShouldBe("text/javascript");

        var serviceWorker = await runtime.Client.GetAsync("/service-worker.js", TestContext.Current.CancellationToken);
        serviceWorker.StatusCode.ShouldBe(HttpStatusCode.OK);
        serviceWorker.Headers.CacheControl?.ToString().ShouldBe("no-cache");

        using var compressedRequest = new HttpRequestMessage(HttpMethod.Get, "/assets/application-a1b2c3.js");
        compressedRequest.Headers.AcceptEncoding.ParseAdd("gzip");
        var compressed = await runtime.Client.SendAsync(compressedRequest, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        compressed.Content.Headers.ContentEncoding.ShouldContain("gzip");
        await using var compressedBody = await compressed.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        await using var decompressedBody = new GZipStream(compressedBody, CompressionMode.Decompress);
        using var reader = new StreamReader(decompressedBody);
        (await reader.ReadToEndAsync(TestContext.Current.CancellationToken)).ShouldContain("unified-runtime-marker");

        var health = await runtime.Client.GetAsync("/health", TestContext.Current.CancellationToken);
        health.StatusCode.ShouldBe(HttpStatusCode.OK);

        root.Headers.GetValues("Content-Security-Policy").Single().ShouldContain("script-src 'self'");
        root.Headers.GetValues("X-Content-Type-Options").Single().ShouldBe("nosniff");
        root.Headers.GetValues("X-Frame-Options").Single().ShouldBe("DENY");
        root.Headers.GetValues("Referrer-Policy").Single().ShouldBe("no-referrer");
        root.Headers.GetValues("Permissions-Policy").Single().ShouldBe("camera=(), microphone=(), geolocation=()");
    }

    private static async Task AssertSpaAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        response.Headers.CacheControl?.ToString().ShouldBe("no-cache");
        (await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).ShouldContain("<div id=\"root\">");
    }

    private sealed class UnifiedRuntime(WebApplication app, string webRoot) : IAsyncDisposable
    {
        public HttpClient Client { get; } = app.GetTestClient();

        public static async Task<UnifiedRuntime> StartAsync(CancellationToken cancellationToken)
        {
            var webRoot = Path.Combine(Path.GetTempPath(), $"melodytrack-unified-runtime-{Guid.NewGuid():N}");
            var assets = Path.Combine(webRoot, "assets");
            Directory.CreateDirectory(assets);
            await File.WriteAllTextAsync(
                Path.Combine(webRoot, "index.html"),
                $"<!doctype html><html><body><div id=\"root\"></div>{new string('x', 2048)}</body></html>",
                cancellationToken);
            await File.WriteAllTextAsync(
                Path.Combine(assets, "application-a1b2c3.js"),
                $"const marker = 'unified-runtime-marker';{new string('x', 4096)}",
                cancellationToken);
            await File.WriteAllTextAsync(Path.Combine(webRoot, "service-worker.js"), "self.addEventListener('fetch', () => {});", cancellationToken);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Production,
                ContentRootPath = webRoot,
                WebRootPath = webRoot
            });
            builder.WebHost.UseTestServer();
            builder.Services.AddHealthChecks();
            builder.Services.AddResponseCompression(options =>
            {
                options.EnableForHttps = true;
                options.Providers.Add<GzipCompressionProvider>();
            });
            builder.Services.Configure<GzipCompressionProviderOptions>(options => options.Level = CompressionLevel.Fastest);

            var app = builder.Build();
            app.UseResponseCompression();
            app.UseUnifiedRuntimeHeaders();
            app.UseSpaStaticFiles();
            app.MapGet("/api/ping", () => TypedResults.Text("pong"));
            app.MapHealthChecks("/health");
            app.MapSpaFallback();
            await app.StartAsync(cancellationToken);
            return new UnifiedRuntime(app, webRoot);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.StopAsync();
            await app.DisposeAsync();
            Directory.Delete(webRoot, recursive: true);
        }
    }
}
