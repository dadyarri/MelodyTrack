using System.Net;
using System.Net.Http.Headers;
using MelodyTrack.Backend.Api.Notifications.Endpoints;
using MelodyTrack.Backend.Api.Notifications.Requests;
using MelodyTrack.Backend.Api.Notifications.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Notifications;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public sealed class NotificationEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task ListAndRead_AuthenticatedStaff_ReturnsOnlyOwnedNotificationsAndPersistsReadState()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var currentUser = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var otherUser = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var currentNotification = CreateNotification(currentUser.Id, null, "Доступное уведомление");
        var otherNotification = CreateNotification(otherUser.Id, null, "Чужое уведомление");
        await db.Notifications.AddRangeAsync([currentNotification, otherNotification], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(currentUser));

        var (listResponse, list) = await App.Client.GETAsync<GetNotificationsEndpoint, GetNotificationsRequest, GetNotificationsResponse>(
            new GetNotificationsRequest());
        var readResponse = await App.Client.POSTAsync<MarkNotificationReadEndpoint, NotificationIdRequest>(
            new NotificationIdRequest { Id = currentNotification.Id });

        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        list.UnreadCount.ShouldBe(1);
        list.Items.ShouldHaveSingleItem().Id.ShouldBe(currentNotification.Id);
        readResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();
        var stored = await db.Notifications.AsNoTracking().SingleAsync(
            notification => notification.Id == currentNotification.Id,
            TestContext.Current.CancellationToken);
        stored.ReadAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task List_ClientPortalPrincipal_ReturnsClientNotificationsWithoutStaffNotifications()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await TestDataFactory.CreateClientAsync(db, "Portal", "Recipient", TestContext.Current.CancellationToken);
        var clientRole = await db.Roles.SingleAsync(
            role => role.RoleName == UserRoles.Client,
            TestContext.Current.CancellationToken);
        var portalUser = new User
        {
            Id = Ulid.NewUlid(),
            FirstName = client.FirstName,
            LastName = client.LastName,
            Email = $"{Ulid.NewUlid()}@portal.invalid",
            Password = "hash",
            Role = clientRole,
            ClientId = client.Id
        };
        var staffUser = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        await db.Users.AddAsync(portalUser, TestContext.Current.CancellationToken);
        await db.Notifications.AddRangeAsync([
            CreateNotification(null, client.Id, "Клиентское уведомление"),
            CreateNotification(staffUser.Id, null, "Служебное уведомление")
        ], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(portalUser));

        var (response, result) = await App.Client.GETAsync<GetNotificationsEndpoint, GetNotificationsRequest, GetNotificationsResponse>(
            new GetNotificationsRequest());

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        result.Items.ShouldHaveSingleItem().Title.ShouldBe("Клиентское уведомление");
    }

    [Fact]
    public async Task PushSubscription_ActiveSession_QueuesDeliveryAndCanBeRevoked()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = "hashed-refresh-token",
            ValidUntil = DateTime.UtcNow.AddDays(1),
            DeviceInfo = "notification-test"
        };
        await db.Sessions.AddAsync(session, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            UserUtils.CreateAccessToken(user, session.Id));
        const string endpoint = "https://push.example.invalid/subscriptions/current-browser";

        var subscribeResponse = await App.Client.POSTAsync<UpsertPushSubscriptionEndpoint, PushSubscriptionRequest>(
            new PushSubscriptionRequest
            {
                Endpoint = endpoint,
                P256Dh = "browser-public-key",
                Auth = "browser-auth-secret"
            });
        var notificationService = scope.ServiceProvider.GetRequiredService<INotificationService>();
        var notification = await notificationService.CreateAsync(new NotificationDraft(
            user.Id,
            null,
            "test.created",
            "Новое уведомление",
            "Подробности доступны в приложении.",
            "В приложении появилось новое уведомление.",
            "/profile"), TestContext.Current.CancellationToken);

        subscribeResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        db.ChangeTracker.Clear();
        var delivery = await db.NotificationPushDeliveries.AsNoTracking().SingleAsync(
            item => item.NotificationId == notification.Id,
            TestContext.Current.CancellationToken);
        delivery.Status.ShouldBe(NotificationPushDeliveryStatus.Pending);

        var revokeResponse = await App.Client.POSTAsync<RevokePushSubscriptionEndpoint, RevokePushSubscriptionRequest>(
            new RevokePushSubscriptionRequest { Endpoint = endpoint });

        revokeResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await db.PushSubscriptions.AsNoTracking().CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    private static Notification CreateNotification(Ulid? userId, Ulid? clientId, string title)
    {
        return new Notification
        {
            Id = Ulid.NewUlid(),
            UserId = userId,
            ClientId = clientId,
            Type = "test.notification",
            Title = title,
            Summary = "Тестовое описание",
            PushMessage = "В приложении появилось новое уведомление.",
            CreatedAtUtc = DateTime.UtcNow
        };
    }
}
