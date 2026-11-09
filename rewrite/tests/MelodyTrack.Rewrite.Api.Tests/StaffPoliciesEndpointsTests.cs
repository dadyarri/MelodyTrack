using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MelodyTrack.Rewrite.Domain;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class StaffPoliciesEndpointsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Actor.Staff Superuser => new(StaffApiHost.Staff, StaffRole.Superuser,
        InitialPermissionPolicies.ForRole(StaffRole.Superuser));
    private static string RolePath => "/api/access/roles/teacher/permissions/clientNotesWrite";
    private static string PersonalPath => $"/api/access/staff/{StaffApiHost.OtherStaff.Value}/permissions/clientNotesWrite";
    private static object Command(Guid commandId) => new
    {
        commandId,
        grant = new { allowed = true, dataScope = "available", timeScope = "any" }
    };

    [Fact]
    public async Task PutRole_AuthenticatedSuperuser_MapsPolicyAndAuthorFromCurrentIdentity()
    {
        await using var host = await StaffApiHost.StartAsync(Superuser, Token);
        var commandId = Guid.NewGuid();

        using var response = await host.SendAsync(HttpMethod.Put, RolePath, new
        {
            commandId,
            authorId = StaffApiHost.OtherStaff.Value,
            role = "superuser",
            grant = new { allowed = true, dataScope = "own", timeScope = "today" }
        }, Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var written = Assert.Single(host.Access.Writes);
        Assert.Equal(commandId, written.CommandId);
        Assert.Equal(StaffApiHost.Staff, written.AuthorId);
        Assert.Equal(StaffRole.Teacher, written.Role);
        Assert.Null(written.TargetId);
        Assert.Equal(StaffPermission.ClientNotesWrite, written.Permission);
        Assert.Equal(new PermissionGrant(true, PermissionDataScope.Own, PermissionTimeScope.Today), written.Grant);
    }

    [Fact]
    public async Task PutPersonal_ExplicitNullGrant_MapsInheritanceWithoutReplacingRolePolicy()
    {
        await using var host = await StaffApiHost.StartAsync(Superuser, Token);
        var commandId = Guid.NewGuid();

        using var response = await host.SendAsync(HttpMethod.Put, PersonalPath,
            new { commandId, grant = (object?)null }, Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var written = Assert.Single(host.Access.Writes);
        Assert.Equal(commandId, written.CommandId);
        Assert.Equal(StaffApiHost.Staff, written.AuthorId);
        Assert.Equal(StaffApiHost.OtherStaff, written.TargetId);
        Assert.Null(written.Role);
        Assert.Null(written.Grant);
    }

    [Fact]
    public async Task PutPersonal_ExplicitDeny_MapsIndividualOverride()
    {
        await using var host = await StaffApiHost.StartAsync(Superuser, Token);

        using var response = await host.SendAsync(HttpMethod.Put, PersonalPath, new
        {
            commandId = Guid.NewGuid(),
            grant = new { allowed = false, dataScope = "available", timeScope = "any" }
        }, Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var written = Assert.Single(host.Access.Writes);
        Assert.Equal(StaffApiHost.OtherStaff, written.TargetId);
        Assert.Equal(PermissionGrant.Denied, written.Grant);
    }

    [Theory]
    [InlineData(false, 401)]
    [InlineData(true, 403)]
    public async Task PutPolicy_InsufficientAccess_RejectsBeforeParsingBody(bool authenticated, int status)
    {
        var administrator = new Actor.Staff(StaffApiHost.Staff, StaffRole.Administrator,
            InitialPermissionPolicies.ForRole(StaffRole.Administrator));
        await using var host = await StaffApiHost.StartAsync(administrator, Token);

        using var response = await SendJsonAsync(host, RolePath, "{malformed", authenticated);

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Empty(host.Access.Writes);
    }

    [Fact]
    public async Task PutPolicy_TeacherHasPoliciesManageGrant_StillRejectsSpecialBoundary()
    {
        var grants = InitialPermissionPolicies.ForRole(StaffRole.Teacher).Grants
            .SetItem(StaffPermission.PoliciesManage, new(true, PermissionDataScope.Available, PermissionTimeScope.Any));
        var teacher = new Actor.Staff(StaffApiHost.Staff, StaffRole.Teacher, new(grants));
        await using var host = await StaffApiHost.StartAsync(teacher, Token);

        using var response = await host.SendAsync(HttpMethod.Put, RolePath, Command(Guid.NewGuid()), Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Access.Writes);
    }

    [Fact]
    public async Task PutRole_SuperuserRole_RejectsWithoutWriting()
    {
        await using var host = await StaffApiHost.StartAsync(Superuser, Token);

        using var response = await host.SendAsync(HttpMethod.Put,
            "/api/access/roles/superuser/permissions/clientNotesWrite", Command(Guid.NewGuid()), Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(host.Access.Writes);
    }

    [Theory]
    [InlineData("/api/access/roles/unknown/permissions/clientNotesWrite")]
    [InlineData("/api/access/roles/teacher/permissions/unknown")]
    [InlineData("/api/access/roles/1/permissions/clientNotesWrite")]
    [InlineData("/api/access/staff/00000000-0000-0000-0000-000000000000/permissions/clientNotesWrite")]
    public async Task PutPolicy_InvalidTarget_RejectsWithoutWriting(string path)
    {
        await using var host = await StaffApiHost.StartAsync(Superuser, Token);

        using var response = await host.SendAsync(HttpMethod.Put, path, Command(Guid.NewGuid()), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Access.Writes);
    }

    [Fact]
    public async Task PutPolicy_EmptyCommandId_RejectsWithoutWriting()
    {
        await using var host = await StaffApiHost.StartAsync(Superuser, Token);

        using var response = await host.SendAsync(HttpMethod.Put, RolePath, Command(Guid.Empty), Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Access.Writes);
    }

    [Theory]
    [InlineData("{\"commandId\":\"019a2c63-8c00-7000-8000-000000000010\"}")]
    [InlineData("{\"commandId\":\"019a2c63-8c00-7000-8000-000000000010\",\"grant\":{\"dataScope\":\"available\",\"timeScope\":\"any\"}}")]
    [InlineData("{\"commandId\":\"019a2c63-8c00-7000-8000-000000000010\",\"grant\":{\"allowed\":true,\"dataScope\":\"unknown\",\"timeScope\":\"any\"}}")]
    [InlineData("{\"commandId\":\"019a2c63-8c00-7000-8000-000000000010\",\"grant\":{\"allowed\":true,\"dataScope\":\"available\",\"timeScope\":\"unknown\"}}")]
    public async Task PutPersonal_MissingOrInvalidGrant_RejectsWithoutRemovingOverride(string json)
    {
        await using var host = await StaffApiHost.StartAsync(Superuser, Token);

        using var response = await SendJsonAsync(host, PersonalPath, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Access.Writes);
    }

    [Fact]
    public async Task PutRole_NullGrant_RejectsWithoutWriting()
    {
        await using var host = await StaffApiHost.StartAsync(Superuser, Token);

        using var response = await host.SendAsync(HttpMethod.Put, RolePath,
            new { commandId = Guid.NewGuid(), grant = (object?)null }, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(host.Access.Writes);
    }

    [Theory]
    [InlineData("forbidden", 403)]
    [InlineData("notFound", 404)]
    [InlineData("invalidState", 409)]
    public async Task PutPolicy_StoreRejectsCurrentTransition_MapsFailureWithoutExposingValues(string error, int status)
    {
        await using var host = await StaffApiHost.StartAsync(Superuser, Token);
        host.Access.WriteError = ErrorList.Single(error switch
        {
            "forbidden" => new ModelError.Forbidden(),
            "notFound" => new ModelError.NotFound(),
            _ => new ModelError.InvalidState()
        });

        using var response = await host.SendAsync(HttpMethod.Put, PersonalPath, Command(Guid.NewGuid()), Token);

        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Single(host.Access.Writes);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    private static async Task<HttpResponseMessage> SendJsonAsync(StaffApiHost host, string path, string json, bool authenticated = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        if (authenticated) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);
        return await host.Client.SendAsync(request, Token);
    }
}
