using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public sealed class AuthorizationPolicyTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task AdministratorPolicy_RoleChangedAfterTokenIssue_UsesCurrentDatabaseRole()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var userRole = await db.Roles.SingleAsync(role => role.RoleName == UserRoles.User, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));
        admin.Role = userRole;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        using var response = await App.Client.PostAsJsonAsync(
            "/auth/invites",
            new CreateInviteRequest { Role = userRole.Id },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ApiAccess_ClientPortalTokenOnStaffEndpoint_ReturnsForbidden()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await TestDataFactory.CreateClientAsync(db, "Portal", "Client", TestContext.Current.CancellationToken);
        var clientRole = await db.Roles.SingleAsync(role => role.RoleName == UserRoles.Client, TestContext.Current.CancellationToken);
        var portalUser = new User
        {
            Id = Ulid.NewUlid(),
            FirstName = client.FirstName,
            LastName = client.LastName,
            Email = $"{Ulid.NewUlid()}@portal.example.test",
            Password = "hash",
            Role = clientRole,
            ClientId = client.Id
        };
        await db.Users.AddAsync(portalUser, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(portalUser));

        using var response = await App.Client.GetAsync("/clients", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
