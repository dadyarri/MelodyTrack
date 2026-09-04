using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.WorkingHoursRequests.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public sealed class WorkingHoursRequestEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task Create_TeacherChangesOwnWorkingDays_CreatesPendingRequestWithoutChangingAvailability()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(teacher));

        using var response = await App.Client.PostAsJsonAsync(
            "/working-hours-requests",
            new { workingHours = WeekSchedule(), message = "Новый учебный график" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<WorkingHoursRequestResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.Status.ShouldBe("pending");
        (await db.WorkingHoursRequests.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await db.UserWorkingHoursDays.CountAsync(item => item.UserId == teacher.Id, TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Approve_SuperuserReviewsPendingRequest_AppliesSnapshotExactlyOnce()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(teacher));
        using var createResponse = await App.Client.PostAsJsonAsync(
            "/working-hours-requests",
            new { workingHours = WeekSchedule(), message = "Новый учебный график" },
            TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<WorkingHoursRequestResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(superuser));

        using var firstResponse = await App.Client.PostAsJsonAsync(
            $"/working-hours-requests/{created.Id}/approve",
            new { expectedVersion = created.Version, message = "Согласовано" },
            TestContext.Current.CancellationToken);
        using var retryResponse = await App.Client.PostAsJsonAsync(
            $"/working-hours-requests/{created.Id}/approve",
            new { expectedVersion = created.Version, message = "Согласовано" },
            TestContext.Current.CancellationToken);

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        retryResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        db.ChangeTracker.Clear();
        var monday = await db.UserWorkingHoursDays.SingleAsync(
            item => item.UserId == teacher.Id && item.DayOfWeek == DayOfWeek.Monday,
            TestContext.Current.CancellationToken);
        monday.StartMinuteOfDay.ShouldBe(8 * 60 + 30);
        (await db.UserWorkingHoursDays.CountAsync(item => item.UserId == teacher.Id, TestContext.Current.CancellationToken)).ShouldBe(7);
        (await db.AuditLogs.AnyAsync(
            item => item.Action == "working_hours_request_approved" && item.EntityId == created.Id.ToString(),
            TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task Approve_AdminAttemptsDecision_ReturnsForbiddenWithoutChangingAvailability()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(teacher));
        using var createResponse = await App.Client.PostAsJsonAsync(
            "/working-hours-requests",
            new { workingHours = WeekSchedule() },
            TestContext.Current.CancellationToken);
        var created = await createResponse.Content.ReadFromJsonAsync<WorkingHoursRequestResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        using var response = await App.Client.PostAsJsonAsync(
            $"/working-hours-requests/{created.Id}/approve",
            new { expectedVersion = created.Version },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await db.UserWorkingHoursDays.CountAsync(item => item.UserId == teacher.Id, TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Create_EquivalentPendingRequest_ReturnsExistingRequest()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));
        var body = new { workingHours = WeekSchedule(), message = "Стабильный график" };

        using var firstResponse = await App.Client.PostAsJsonAsync(
            "/working-hours-requests", body, TestContext.Current.CancellationToken);
        using var secondResponse = await App.Client.PostAsJsonAsync(
            "/working-hours-requests", body, TestContext.Current.CancellationToken);
        var first = await firstResponse.Content.ReadFromJsonAsync<WorkingHoursRequestResponse>(
            cancellationToken: TestContext.Current.CancellationToken);
        var second = await secondResponse.Content.ReadFromJsonAsync<WorkingHoursRequestResponse>(
            cancellationToken: TestContext.Current.CancellationToken);

        first.ShouldNotBeNull();
        second.ShouldNotBeNull();
        second.Id.ShouldBe(first.Id);
        (await db.WorkingHoursRequests.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    private static object[] WeekSchedule() =>
    [
        new { dayOfWeek = "monday", isWorkingDay = true, startTime = (string?)"08:30", endTime = (string?)"17:15" },
        new { dayOfWeek = "tuesday", isWorkingDay = true, startTime = (string?)"09:00", endTime = (string?)"18:00" },
        new { dayOfWeek = "wednesday", isWorkingDay = true, startTime = (string?)"09:00", endTime = (string?)"18:00" },
        new { dayOfWeek = "thursday", isWorkingDay = true, startTime = (string?)"09:00", endTime = (string?)"18:00" },
        new { dayOfWeek = "friday", isWorkingDay = true, startTime = (string?)"09:00", endTime = (string?)"18:00" },
        new { dayOfWeek = "saturday", isWorkingDay = false, startTime = (string?)null, endTime = (string?)null },
        new { dayOfWeek = "sunday", isWorkingDay = false, startTime = (string?)null, endTime = (string?)null }
    ];
}
