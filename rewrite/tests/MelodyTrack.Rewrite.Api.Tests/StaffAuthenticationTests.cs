using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using MelodyTrack.Rewrite.Api;
using MelodyTrack.Rewrite.Domain;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class StaffAuthenticationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Actor.Staff Administrator => new(StaffApiHost.Staff, StaffRole.Administrator,
        InitialPermissionPolicies.ForRole(StaffRole.Administrator));

    [Theory]
    [InlineData(null)]
    [InlineData("Bearer malformed")]
    [InlineData("Basic malformed")]
    public async Task Post_InvalidCredentials_RejectsBeforeParsingBody(string? authorization)
    {
        var expenses = new StaffApiHost.ExpenseState();
        await using var host = await StaffApiHost.StartAsync(Administrator, Token, expenses: expenses);

        using var result = await SendMalformedAsync(host, authorization);

        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Contains(result.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
        Assert.Equal("application/problem+json", result.Content.Headers.ContentType!.MediaType);
        Assert.Equal(0, host.Access.Reads);
        Assert.Equal(0, expenses.Writes);
    }

    [Fact]
    public async Task Post_UnknownCanonicalToken_RejectsBeforeParsingBody()
    {
        var expenses = new StaffApiHost.ExpenseState();
        await using var host = await StaffApiHost.StartAsync(Administrator, Token, expenses: expenses);

        using var result = await SendMalformedAsync(host, $"Bearer {new string('B', 64)}");

        Assert.Equal(HttpStatusCode.Unauthorized, result.StatusCode);
        Assert.Equal(1, host.Tokens.Reads);
        Assert.Equal(0, host.Access.Reads);
        Assert.Equal(0, expenses.Writes);
    }

    [Fact]
    public async Task Post_ActionDenied_RejectsBeforeParsingBody()
    {
        var expenses = new StaffApiHost.ExpenseState();
        var teacher = new Actor.Staff(StaffApiHost.Staff, StaffRole.Teacher, InitialPermissionPolicies.ForRole(StaffRole.Teacher));
        await using var host = await StaffApiHost.StartAsync(teacher, Token, expenses: expenses);

        using var result = await SendMalformedAsync(host, $"Bearer {StaffApiHost.BearerToken}");

        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.Equal(1, host.Access.Reads);
        Assert.Equal(0, expenses.Writes);
    }

    [Fact]
    public async Task Get_ValidToken_ContainsIdentityWithoutRoleOrPermissions()
    {
        await using var host = await StaffApiHost.StartAsync(Administrator, Token);
        using var request = AuthorizedGet();
        request.Headers.Add("X-Role", "Superuser");

        using var result = await host.Client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var identity = await result.Content.ReadFromJsonAsync<StaffApiHost.IdentityProbe>(Token);
        Assert.Equal(StaffApiHost.Staff.Value, identity!.StaffId);
        Assert.Equal(2, identity.Claims.Length);
        Assert.Contains(identity.Claims, claim => claim.Type == ClaimTypes.NameIdentifier && claim.Value == StaffApiHost.Staff.Value.ToString());
        Assert.Contains(identity.Claims, claim => claim.Type == ClientNotesEndpoints.ActorKindClaim && claim.Value == "staff");
        Assert.DoesNotContain(identity.Claims, claim => claim.Type == ClaimTypes.Role || claim.Type.Contains("permission", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Post_RoleChangesBetweenRequests_UsesCurrentPolicy()
    {
        await using var host = await StaffApiHost.StartAsync(Administrator, Token);
        using var before = await SendMalformedAsync(host, $"Bearer {StaffApiHost.BearerToken}");
        Assert.Equal(HttpStatusCode.BadRequest, before.StatusCode);
        host.Access.Actor = new(StaffApiHost.Staff, StaffRole.Teacher, InitialPermissionPolicies.ForRole(StaffRole.Teacher));

        using var after = await SendMalformedAsync(host, $"Bearer {StaffApiHost.BearerToken}");

        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
        Assert.Equal(2, host.Access.Reads);
    }

    [Fact]
    public async Task Get_SessionRevokedAfterSuccess_RejectsSameToken()
    {
        await using var host = await StaffApiHost.StartAsync(Administrator, Token);
        using var firstRequest = AuthorizedGet();
        using var first = await host.Client.SendAsync(firstRequest, Token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        host.Tokens.Revoked = true;
        using var nextRequest = AuthorizedGet();

        using var next = await host.Client.SendAsync(nextRequest, Token);

        Assert.Equal(HttpStatusCode.Unauthorized, next.StatusCode);
        Assert.Equal(2, host.Tokens.Reads);
        Assert.Equal(1, host.Access.Reads);
    }

    [Fact]
    public async Task Get_CredentialsRemovedAfterSuccess_DoesNotReusePreviousIdentity()
    {
        await using var host = await StaffApiHost.StartAsync(Administrator, Token);
        using var firstRequest = AuthorizedGet();
        using var first = await host.Client.SendAsync(firstRequest, Token);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        using var next = await host.Client.GetAsync("/_test/identity", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, next.StatusCode);
        Assert.Equal(1, host.Access.Reads);
    }

    [Fact]
    public async Task Get_AccessResolverReturnsDifferentSubject_RejectsRequest()
    {
        var different = Administrator with { Id = new StaffId(Guid.Parse("019a2c63-8c00-7000-8000-000000000099")) };
        await using var host = await StaffApiHost.StartAsync(different, Token);
        using var request = AuthorizedGet();

        using var result = await host.Client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
    }

    private static HttpRequestMessage AuthorizedGet()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/_test/identity");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);
        return request;
    }

    private static async Task<HttpResponseMessage> SendMalformedAsync(StaffApiHost host, string? authorization)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/expenses")
        {
            Content = new StringContent("{malformed", Encoding.UTF8, "application/json")
        };
        if (authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return await host.Client.SendAsync(request, Token);
    }
}
