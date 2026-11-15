using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using MelodyTrack.Rewrite.Domain;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class StaffSessionEndpointsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Actor.Staff Administrator => new(StaffApiHost.Staff, StaffRole.Administrator,
        InitialPermissionPolicies.ForRole(StaffRole.Administrator));

    [Fact]
    public async Task GetMe_CurrentSession_ReturnsOwnIdentityAndScopedGrantsWithoutSecrets()
    {
        await using var host = await StaffApiHost.StartAsync(Administrator, Token);

        using var response = await SendAsync(host, HttpMethod.Get, "/api/auth/me", StaffApiHost.BearerToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var json = await response.Content.ReadAsStringAsync(Token);
        using var document = JsonDocument.Parse(json);
        var current = document.RootElement;
        Assert.Equal(StaffApiHost.Staff.Value, current.GetProperty("staffId").GetGuid());
        Assert.Equal("fixture-staff", current.GetProperty("login").GetString());
        Assert.Equal("administrator", current.GetProperty("role").GetString());
        var expenses = Assert.Single(current.GetProperty("permissions").EnumerateArray(),
            permission => permission.GetProperty("permission").GetString() == "expensesRecord");
        Assert.True(expenses.GetProperty("allowed").GetBoolean());
        Assert.Equal("available", expenses.GetProperty("dataScope").GetString());
        Assert.Equal("today", expenses.GetProperty("timeScope").GetString());
        Assert.Equal(Enum.GetValues<StaffPermission>().Length, current.GetProperty("permissions").GetArrayLength());
        Assert.False(current.TryGetProperty("sessionId", out _));
        Assert.DoesNotContain(StaffApiHost.BearerToken, json);
        Assert.DoesNotContain("tokenHash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMe_AnotherStaffIdInQuery_ReturnsOnlyAuthenticatedStaffLogin()
    {
        await using var host = await StaffApiHost.StartAsync(Administrator, Token);

        using var response = await SendAsync(host, HttpMethod.Get, $"/api/auth/me?staffId={StaffApiHost.OtherStaff.Value}", StaffApiHost.BearerToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal("fixture-staff", document.RootElement.GetProperty("login").GetString());
        Assert.Equal(StaffApiHost.Staff, Assert.Single(host.Logins.Reads));
    }

    [Fact]
    public async Task GetMe_MissingCredentialLogin_ReturnsUniformUnauthorized()
    {
        await using var host = await StaffApiHost.StartAsync(Administrator, Token);
        host.Logins.Values.Remove(StaffApiHost.Staff);

        using var response = await SendAsync(host, HttpMethod.Get, "/api/auth/me", StaffApiHost.BearerToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains(response.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal("Требуется вход", document.RootElement.GetProperty("title").GetString());
        Assert.False(document.RootElement.TryGetProperty("login", out _));
    }

    [Fact]
    public void ToString_CurrentIdentity_OmitsLoginAndStaffId()
    {
        var response = new CurrentStaffResponse(StaffApiHost.Staff.Value, "fixture-staff", "administrator", []);

        Assert.Equal(nameof(CurrentStaffResponse), response.ToString());
    }

    [Fact]
    public async Task GetMe_PolicyChangesBetweenRequests_ReturnsCurrentGrants()
    {
        await using var host = await StaffApiHost.StartAsync(Administrator, Token);
        using var first = await SendAsync(host, HttpMethod.Get, "/api/auth/me", StaffApiHost.BearerToken);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        host.Access.Actor = new(StaffApiHost.Staff, StaffRole.Teacher, InitialPermissionPolicies.ForRole(StaffRole.Teacher));

        using var response = await SendAsync(host, HttpMethod.Get, "/api/auth/me", StaffApiHost.BearerToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        Assert.Equal("teacher", document.RootElement.GetProperty("role").GetString());
        var expenses = Assert.Single(document.RootElement.GetProperty("permissions").EnumerateArray(),
            permission => permission.GetProperty("permission").GetString() == "expensesRecord");
        Assert.False(expenses.GetProperty("allowed").GetBoolean());
        Assert.Equal(2, host.Access.Reads);
    }

    [Theory]
    [InlineData("GET", "/api/auth/me")]
    [InlineData("POST", "/api/auth/logout")]
    public async Task Request_NoCredentials_RejectsWithoutRevokingSession(string method, string path)
    {
        await using var host = await StaffApiHost.StartAsync(Administrator, Token);

        using var response = await SendAsync(host, new(method), path, null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Null(host.Tokens.LastRevokedSession);
        Assert.Equal(0, host.Access.Reads);
    }

    [Fact]
    public async Task Logout_CurrentSession_RevokesAllItsTokensAndPreservesOtherSessionsAndAccounts()
    {
        await using var host = await StaffApiHost.StartAsync(Administrator, Token);
        host.Access.OtherActors.Add(StaffApiHost.OtherStaff,
            new(StaffApiHost.OtherStaff, StaffRole.Teacher, InitialPermissionPolicies.ForRole(StaffRole.Teacher)));

        using var response = await host.SendAsync(HttpMethod.Post, "/api/auth/logout",
            new { sessionId = StaffApiHost.OtherSession, staffId = StaffApiHost.OtherStaff.Value }, Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(StaffApiHost.Session, host.Tokens.LastRevokedSession);
        using var current = await SendAsync(host, HttpMethod.Get, "/api/auth/me", StaffApiHost.BearerToken);
        using var sibling = await SendAsync(host, HttpMethod.Get, "/api/auth/me", StaffApiHost.SameSessionToken);
        using var otherDevice = await SendAsync(host, HttpMethod.Get, "/api/auth/me", StaffApiHost.OtherSessionToken);
        using var otherAccount = await SendAsync(host, HttpMethod.Get, "/api/auth/me", StaffApiHost.OtherStaffToken);
        Assert.Equal(HttpStatusCode.Unauthorized, current.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, sibling.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherDevice.StatusCode);
        Assert.Equal(HttpStatusCode.OK, otherAccount.StatusCode);
    }

    [Fact]
    public async Task Logout_AlreadyRevokedToken_RejectsSubsequentLogout()
    {
        await using var host = await StaffApiHost.StartAsync(Administrator, Token);
        using var first = await SendAsync(host, HttpMethod.Post, "/api/auth/logout", StaffApiHost.BearerToken);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        using var repeated = await SendAsync(host, HttpMethod.Post, "/api/auth/logout", StaffApiHost.BearerToken);

        Assert.Equal(HttpStatusCode.Unauthorized, repeated.StatusCode);
        Assert.False(repeated.Headers.Contains("Set-Cookie"));
    }

    private static async Task<HttpResponseMessage> SendAsync(StaffApiHost host, HttpMethod method, string path, string? bearer)
    {
        using var request = new HttpRequestMessage(method, path);
        if (bearer is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
        return await host.Client.SendAsync(request, Token);
    }
}
