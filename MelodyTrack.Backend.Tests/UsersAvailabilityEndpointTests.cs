using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Users.Endpoints;
using MelodyTrack.Backend.Api.Users.Responses;
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
public class UsersAvailabilityEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task GetUsersAvailability_ReturnsAllUsersAvailabilityForAdmin()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var teacherWithoutCustomHours = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);

        await db.UserWorkingHoursDays.AddAsync(new UserWorkingHoursDay
        {
            Id = Ulid.NewUlid(),
            UserId = teacher.Id,
            User = teacher,
            DayOfWeek = DayOfWeek.Monday,
            IsWorkingDay = true,
            StartMinuteOfDay = 9 * 60,
            EndMinuteOfDay = 21 * 60
        }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var (response, content) = await App.Client.GETAsync<GetUsersAvailabilityEndpoint, EmptyRequest, GetUsersAvailabilityResponse>(
            EmptyRequest.Instance);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        content.ShouldNotBeNull();
        content.Availabilities.ShouldContain(item => item.UserId == teacher.Id && item.WorkingHours.Any(hour =>
            hour.DayOfWeek == "monday" && hour.StartTime == "09:00" && hour.EndTime == "21:00"));
        content.Availabilities.ShouldContain(item => item.UserId == teacherWithoutCustomHours.Id && item.WorkingHours.Count == 7);
    }

    [Fact]
    public async Task GetUsersAvailability_ReturnsForbiddenForRegularUser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.GetAsync("/users/availability", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUserAvailability_ReturnsForbiddenForOtherRegularUser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var currentUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var otherUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(currentUser));

        var response = await App.Client.GetAsync($"/users/{otherUser.Id}/availability", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetUserAvailability_ReturnsOwnAvailabilityForRegularUser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.GetAsync($"/users/{user.Id}/availability", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetUserAvailability_ReturnsLastActivity()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var activityId = Ulid.NewUlid();

        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                Id = activityId,
                CreatedAtUtc = DateTime.UtcNow,
                Category = "users",
                Action = "user_availability_updated",
                EntityType = "user_availability",
                EntityId = user.Id.ToString(),
                Details = "Availability updated"
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.GetAsync($"/users/{user.Id}/availability", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<UserAvailabilityResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.LastActivity.ShouldNotBeNull();
        payload.LastActivity.Id.ShouldBe(activityId);
    }

    [Fact]
    public async Task UpdateUserAvailability_ReturnsConflictWhenExpectedActivityIdIsStale()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var activityId = Ulid.NewUlid();

        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                Id = activityId,
                CreatedAtUtc = DateTime.UtcNow,
                Category = "users",
                Action = "user_availability_updated",
                EntityType = "user_availability",
                EntityId = user.Id.ToString(),
                Details = "Availability updated"
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.PutAsJsonAsync(
            $"/users/{user.Id}/availability",
            new
            {
                workingHours = new[]
                {
                    new { dayOfWeek = "monday", isWorkingDay = true, startTime = (string?)"09:00", endTime = (string?)"18:00" },
                    new { dayOfWeek = "tuesday", isWorkingDay = true, startTime = (string?)"09:00", endTime = (string?)"18:00" },
                    new { dayOfWeek = "wednesday", isWorkingDay = true, startTime = (string?)"09:00", endTime = (string?)"18:00" },
                    new { dayOfWeek = "thursday", isWorkingDay = true, startTime = (string?)"09:00", endTime = (string?)"18:00" },
                    new { dayOfWeek = "friday", isWorkingDay = true, startTime = (string?)"09:00", endTime = (string?)"18:00" },
                    new { dayOfWeek = "saturday", isWorkingDay = false, startTime = (string?)null, endTime = (string?)null },
                    new { dayOfWeek = "sunday", isWorkingDay = false, startTime = (string?)null, endTime = (string?)null },
                },
                vacations = Array.Empty<object>(),
                expectedActivityId = Ulid.NewUlid(),
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var payload = await response.Content.ReadFromJsonAsync<StaleEntityConflictResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.EntityType.ShouldBe("user_availability");
        payload.CurrentActivity.ShouldNotBeNull();
        payload.CurrentActivity.Id.ShouldBe(activityId);
    }
}
