using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Users.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class UserEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task UpdateUser_AllowsUserToUpdateOwnContacts()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.PutAsJsonAsync(
            $"/users/{user.Id}",
            new
            {
                firstName = user.FirstName,
                lastName = user.LastName,
                phone = "+4915123456789",
                telegram = "https://t.me/schedule_operator",
                vk = "https://vk.com/schedule.operator"
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();
        var updatedUser = await db.Users.AsNoTracking().FirstAsync(item => item.Id == user.Id, TestContext.Current.CancellationToken);
        updatedUser.Phone.ShouldBe("+4915123456789");
        updatedUser.Telegram.ShouldBe("https://t.me/schedule_operator");
        updatedUser.Vk.ShouldBe("https://vk.com/schedule.operator");
    }

    [Fact]
    public async Task UpdateUser_ReturnsForbiddenForOtherRegularUser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var currentUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var otherUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(currentUser));

        var response = await App.Client.PutAsJsonAsync(
            $"/users/{otherUser.Id}",
            new
            {
                firstName = otherUser.FirstName,
                lastName = otherUser.LastName,
                phone = "+4915123456789"
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUsers_ReturnsLastActivityForAdmin()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var activityId = Ulid.NewUlid();

        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                Id = activityId,
                CreatedAtUtc = DateTime.UtcNow,
                Category = "users",
                Action = "user_updated",
                EntityType = "user",
                EntityId = user.Id.ToString(),
                Details = "User updated"
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var response = await App.Client.GetAsync("/users", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<GetUsersResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        var updatedUser = payload.Users.FirstOrDefault(item => item.Id == user.Id);
        updatedUser.ShouldNotBeNull();
        updatedUser.LastActivity.ShouldNotBeNull();
        updatedUser.LastActivity.Id.ShouldBe(activityId);
    }

    [Fact]
    public async Task GetMe_ReturnsContactsAndLastActivity()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        user.Phone = "+4915123456789";
        user.Telegram = "https://t.me/schedule_operator";
        user.Vk = "https://vk.com/schedule.operator";

        var activityId = Ulid.NewUlid();
        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                Id = activityId,
                CreatedAtUtc = DateTime.UtcNow,
                Category = "users",
                Action = "user_updated",
                EntityType = "user",
                EntityId = user.Id.ToString(),
                Details = "User updated"
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.GetAsync("/auth/me", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<MeResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.Phone.ShouldBe(user.Phone);
        payload.Telegram.ShouldBe(user.Telegram);
        payload.Vk.ShouldBe(user.Vk);
        payload.LastActivity.ShouldNotBeNull();
        payload.LastActivity.Id.ShouldBe(activityId);
    }
}
