using MelodyTrack.Rewrite.Api;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class StaffSessionCookiesTests
{
    [Theory]
    [InlineData("Production", "localhost", "http", true)]
    [InlineData("Development", "localhost", "http", false)]
    [InlineData("Testing", "127.0.0.1", "http", false)]
    [InlineData("Testing", "[::1]", "http", false)]
    [InlineData("Development", "school.example.test", "http", true)]
    [InlineData("Development", "localhost", "https", true)]
    public void Write_EnvironmentAndTransport_UsesRestrictedPersistentCookie(string environment, string host,
        string scheme, bool secure)
    {
        using var request = new Request(environment, host, scheme);

        StaffSessionCookies.Write(request.Http, new string('S', 64));

        var cookie = SetCookieHeaderValue.Parse(request.Http.Response.Headers.SetCookie.ToString());
        Assert.Equal(StaffSessionCookies.Name, cookie.Name.ToString());
        Assert.True(cookie.HttpOnly);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal("/api/auth", cookie.Path.ToString());
        Assert.Equal(TimeSpan.FromDays(14), cookie.MaxAge);
        Assert.Equal(secure, cookie.Secure);
        Assert.False(cookie.Domain.HasValue);
    }

    [Fact]
    public void Clear_ExistingCookie_ExpiresMatchingNameAndPath()
    {
        using var request = new Request("Production", "school.example.test", "https");

        StaffSessionCookies.Clear(request.Http);

        var cookie = SetCookieHeaderValue.Parse(request.Http.Response.Headers.SetCookie.ToString());
        Assert.Equal(StaffSessionCookies.Name, cookie.Name.ToString());
        Assert.Equal("/api/auth", cookie.Path.ToString());
        Assert.True(cookie.HttpOnly);
        Assert.True(cookie.Secure);
        Assert.Equal(Microsoft.Net.Http.Headers.SameSiteMode.Strict, cookie.SameSite);
        Assert.Equal(DateTimeOffset.UnixEpoch, cookie.Expires);
        Assert.False(cookie.Domain.HasValue);
    }

    [Theory]
    [InlineData("http://localhost:5280", true)]
    [InlineData("HTTP://LOCALHOST:5280", true)]
    [InlineData("http://localhost:5174", true)]
    [InlineData("https://school.example.test", true)]
    [InlineData(null, false)]
    [InlineData("null", false)]
    [InlineData("https://foreign.example.test", false)]
    [InlineData("http://localhost:5281", false)]
    [InlineData("https://localhost:5280", false)]
    [InlineData("http://localhost:5280/path", false)]
    [InlineData("http://localhost:5280?query=value", false)]
    [InlineData("http://localhost:5280#fragment", false)]
    [InlineData("http://user@localhost:5280", false)]
    [InlineData("file:///localhost:5280", false)]
    public void HasTrustedOrigin_OriginHeader_RequiresSameOrConfiguredOrigin(string? origin, bool allowed)
    {
        using var request = new Request("Development", "localhost:5280", "http");
        if (origin is not null) request.Http.Request.Headers.Origin = origin;

        var result = StaffSessionCookies.HasTrustedOrigin(request.Http);

        Assert.Equal(allowed, result);
    }

    private sealed class Request : IDisposable
    {
        private readonly ServiceProvider services;
        public DefaultHttpContext Http { get; } = new();

        public Request(string environment, string host, string scheme)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:Passkeys:Origins:0"] = "http://localhost:5174",
                ["Authentication:Passkeys:Origins:1"] = "https://school.example.test"
            }).Build();
            services = new ServiceCollection().AddSingleton<IConfiguration>(configuration)
                .AddSingleton<IHostEnvironment>(new Environment(environment)).BuildServiceProvider();
            Http.RequestServices = services;
            Http.Request.Host = new HostString(host);
            Http.Request.Scheme = scheme;
        }

        public void Dispose() => services.Dispose();
    }

    private sealed class Environment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Cookie tests";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
