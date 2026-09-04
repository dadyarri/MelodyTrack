using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
    public async Task UpdateUserAvailability_SuperuserDirectManagement_PersistsCompleteWeekVacationAndAudit()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(superuser));

        using var response = await App.Client.PutAsJsonAsync(
            $"/users/{user.Id}/availability",
            new
            {
                workingHours = WeekSchedule(),
                vacations = new[] { new { startDate = "2026-08-10T09:30:00Z", endDate = "2026-08-16T18:15:00Z" } },
                expectedActivityId = (Ulid?)null
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        db.ChangeTracker.Clear();
        var stored = await db.Users.Include(item => item.WorkingHours).Include(item => item.Vacations)
            .SingleAsync(item => item.Id == user.Id, TestContext.Current.CancellationToken);
        stored.WorkingHours.Count.ShouldBe(7);
        var monday = stored.WorkingHours.Single(item => item.DayOfWeek == DayOfWeek.Monday);
        monday.IsWorkingDay.ShouldBeTrue();
        monday.StartMinuteOfDay.ShouldBe(8 * 60 + 30);
        monday.EndMinuteOfDay.ShouldBe(17 * 60 + 15);
        stored.Vacations.ShouldHaveSingleItem().StartDate.ShouldBe(new DateTime(2026, 8, 10, 9, 30, 0, DateTimeKind.Utc));
        (await db.AuditLogs.AnyAsync(
            item => item.Action == "user_vacations_updated_directly" && item.EntityId == user.Id.ToString(),
            TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task UpdateUserAvailability_TeacherAddsOwnVacation_ReturnsForbidden()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        using var response = await App.Client.PutAsJsonAsync(
            $"/users/{user.Id}/availability",
            new
            {
                workingHours = WeekSchedule(),
                vacations = new[] { new { startDate = "2026-08-10T09:30:00Z", endDate = "2026-08-16T18:15:00Z" } },
                expectedActivityId = (Ulid?)null
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await db.UserVacations.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task UpdateUserAvailability_ConflictingAppointment_RequiresExplicitCancellation()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Мария", "Соколова", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Вокал", TestContext.Current.CancellationToken);
        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = user,
            StartDate = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 8, 12, 11, 0, 0, DateTimeKind.Utc),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };
        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(superuser));
        var input = new
        {
            workingHours = WeekSchedule(),
            vacations = new[] { new { startDate = "2026-08-10T09:30:00Z", endDate = "2026-08-30T18:15:00Z" } },
            expectedActivityId = (Ulid?)null,
            cancelConflictingAppointments = false
        };

        using var conflictResponse = await App.Client.PutAsJsonAsync(
            $"/users/{user.Id}/availability",
            input,
            TestContext.Current.CancellationToken);

        conflictResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        db.ChangeTracker.Clear();
        (await db.Appointments.SingleAsync(item => item.Id == appointment.Id, TestContext.Current.CancellationToken)).Status
            .ShouldBe(AppointmentStatus.Planned);
        (await db.UserVacations.CountAsync(item => item.UserId == user.Id, TestContext.Current.CancellationToken)).ShouldBe(0);

        using var successResponse = await App.Client.PutAsJsonAsync(
            $"/users/{user.Id}/availability",
            new
            {
                input.workingHours,
                input.vacations,
                input.expectedActivityId,
                cancelConflictingAppointments = true
            },
            TestContext.Current.CancellationToken);

        successResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        db.ChangeTracker.Clear();
        (await db.Appointments.SingleAsync(item => item.Id == appointment.Id, TestContext.Current.CancellationToken)).Status
            .ShouldBe(AppointmentStatus.Cancelled);
        (await db.UserVacations.SingleAsync(item => item.UserId == user.Id, TestContext.Current.CancellationToken)).EndDate
            .ShouldBe(new DateTime(2026, 8, 30, 18, 15, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task GetVacationAppointmentConflictCount_SelectedRange_ReturnsOnlyOverlappingPlannedAppointments()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var otherUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Мария", "Соколова", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Вокал", TestContext.Current.CancellationToken);
        var rangeStart = new DateTime(2026, 8, 12, 9, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);

        await db.Appointments.AddRangeAsync(
        [
            new Appointment
            {
                Id = Ulid.NewUlid(), Client = client, Service = service, Provider = user,
                StartDate = rangeStart.AddMinutes(30), EndDate = rangeStart.AddMinutes(90),
                Status = AppointmentStatus.Planned, IsDeleted = false
            },
            new Appointment
            {
                Id = Ulid.NewUlid(), Client = client, Service = service, Provider = user,
                StartDate = rangeStart.AddMinutes(45), EndDate = rangeStart.AddMinutes(105),
                Status = AppointmentStatus.Completed, IsDeleted = false
            },
            new Appointment
            {
                Id = Ulid.NewUlid(), Client = client, Service = service, Provider = user,
                StartDate = rangeEnd, EndDate = rangeEnd.AddHours(1),
                Status = AppointmentStatus.Planned, IsDeleted = false
            },
            new Appointment
            {
                Id = Ulid.NewUlid(), Client = client, Service = service, Provider = otherUser,
                StartDate = rangeStart.AddMinutes(30), EndDate = rangeStart.AddMinutes(90),
                Status = AppointmentStatus.Planned, IsDeleted = false
            }
        ], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(superuser));

        using var response = await App.Client.GetAsync(
            $"/users/{user.Id}/vacation-appointment-conflict-count?startDate={rangeStart:O}&endDate={rangeEnd:O}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<int>(cancellationToken: TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task UpdateUserAvailability_TeacherChangesOwnWorkingDays_ReturnsForbidden()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        using var response = await App.Client.PutAsJsonAsync(
            $"/users/{user.Id}/availability",
            new
            {
                workingHours = WeekSchedule(),
                vacations = Array.Empty<object>(),
                expectedActivityId = (Ulid?)null
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await db.UserWorkingHoursDays.CountAsync(item => item.UserId == user.Id, TestContext.Current.CancellationToken)).ShouldBe(0);
    }

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
    public async Task GetUsersAvailability_AsAdmin_DoesNotExposeSuperusers()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var (response, content) = await App.Client.GETAsync<GetUsersAvailabilityEndpoint, EmptyRequest, GetUsersAvailabilityResponse>(
            EmptyRequest.Instance);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        content.ShouldNotBeNull();
        content.Availabilities.ShouldNotContain(item => item.UserId == superuser.Id);
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
    public async Task GetUserAvailability_ReturnsForbiddenForAdminWhenTargetIsSuperuser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var response = await App.Client.GetAsync($"/users/{superuser.Id}/availability", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
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

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(superuser));

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

    [Fact]
    public async Task UpdateUserAvailability_ReturnsForbiddenForAdminWhenTargetIsSuperuser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var response = await App.Client.PutAsJsonAsync(
            $"/users/{superuser.Id}/availability",
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
                expectedActivityId = (Ulid?)null,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
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
