using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Schedule.Endpoints;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Api.Schedule.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class ScheduleEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task GetMiniSchedule_ReturnsOnlyCurrentUsersUpcomingAppointments()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var currentUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var otherUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);
        var nowUtc = DateTime.UtcNow;

        var visibleAppointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = currentUser,
            StartDate = nowUtc.AddMinutes(30),
            EndDate = nowUtc.AddMinutes(90),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };

        var pastAppointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = currentUser,
            StartDate = nowUtc.AddHours(-2),
            EndDate = nowUtc.AddMinutes(-10),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };

        var otherUsersAppointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = otherUser,
            StartDate = nowUtc.AddHours(2),
            EndDate = nowUtc.AddHours(3),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };

        await db.Appointments.AddRangeAsync(
            [visibleAppointment, pastAppointment, otherUsersAppointment],
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(currentUser));

        var (rsp, res) = await App.Client.GETAsync<GetMiniScheduleEndpoint, BaseGetAppointmentsRequest, GetMiniScheduleResponse>(
            new BaseGetAppointmentsRequest
            {
                Timezone = "UTC"
            });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();

        var returnedAppointments = res.Appointments.SelectMany(day => day.Value).ToList();

        returnedAppointments.Count.ShouldBe(1);
        returnedAppointments[0].Id.ShouldBe(visibleAppointment.Id);
        returnedAppointments[0].Provider.ShouldNotBeNull();
        returnedAppointments[0].Provider!.Id.ShouldBe(currentUser.Id);
    }

    [Theory]
    [InlineData("single")]
    [InlineData("weekday-this-and-following")]
    [InlineData("weekday-all")]
    [InlineData("this-and-following")]
    [InlineData("all")]
    public async Task DeleteAppointment_AcceptsAllSupportedRecurringScopes(string scope)
    {
        await using var serviceScope = App.Services.CreateAsyncScope();
        var db = serviceScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = serviceScope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();

        var currentUser = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var rule = await TestDataFactory.CreateWeeklyRuleAsync(
            db,
            new DateTime(2025, 11, 17, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 10, 23, 59, 59, DateTimeKind.Utc),
            1 + 4,
            "Artem",
            "Volkov",
            "Drums",
            TestContext.Current.CancellationToken);

        await materializer.EnsureAppointmentsGeneratedAsync(
            new DateTime(2025, 11, 17, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 10, 23, 59, 59, DateTimeKind.Utc),
            TestContext.Current.CancellationToken);

        var occurrenceId = await db.Appointments
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == new DateTime(2025, 11, 26, 12, 0, 0, DateTimeKind.Utc))
            .Select(item => item.Id)
            .FirstAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(currentUser));

        var response = await App.Client.DeleteAsync($"/appointments/{occurrenceId}?scope={scope}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task UpdateAppointment_WritesRussianStatusLabelsToAudit()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Иванова", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Вокал", TestContext.Current.CancellationToken);

        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = new DateTime(2026, 05, 24, 12, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 05, 24, 13, 0, 0, DateTimeKind.Utc),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };

        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, _) = await App.Client.PATCHAsync<UpdateAppointmentEndpoint, UpdateAppointmentRequest, NoContent>(new UpdateAppointmentRequest
        {
            Id = appointment.Id,
            Status = "completed"
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();

        var auditLog = await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstAsync(
                item => item.Action == "appointment_updated" && item.EntityId == appointment.Id.ToString(),
                TestContext.Current.CancellationToken);

        auditLog.Details.ShouldBe("Клиент: Иванова Анна; Услуга: Вокал; Преподаватель: —; Тема курса: —; Начало: 2026-05-24T12:00:00.0000000Z; Статус: Запланировано → Завершено");
    }

    [Fact]
    public async Task CreateRecurringAppointment_AllowsOpenEndedSeriesWithoutPatternEndDate()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(
            type => type.Type == AppointmentRecurrenceType.Weekly,
            TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, res) = await App.Client.POSTAsync<CreateAppointmentEndpoint, CreateAppointmentRequest, CreateEntityResponse>(
            new CreateAppointmentRequest
            {
                ClientId = client.Id,
                ServiceId = service.Id,
                StartDate = new DateTime(2026, 05, 26, 12, 0, 0, DateTimeKind.Utc),
                Timezone = "UTC",
                RecurrenceTypeId = recurrenceType.Id,
                PatternEndDate = null,
                RecurrencePattern = 2 + 8
            });

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();

        db.ChangeTracker.Clear();

        var appointment = await db.Appointments
            .Include(item => item.RecurringRule)
            .FirstAsync(item => item.Id == res.Id, TestContext.Current.CancellationToken);

        appointment.RecurringRule.ShouldNotBeNull();
        appointment.RecurringRule!.EndDate.ShouldBeNull();
        appointment.RecurringRule.RecurrencePattern.ShouldBe(2 + 8);
    }

    [Fact]
    public async Task CreateAppointment_AllowsLinkingCourseThemeAndLessonNotes()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Mira", "Tempo", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Piano lesson", TestContext.Current.CancellationToken);

        var course = new Course
        {
            Id = Ulid.NewUlid(),
            Name = "Piano foundations",
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
        var block = new CourseBlock
        {
            Id = Ulid.NewUlid(),
            Course = course,
            CourseId = course.Id,
            Title = "Block 1",
            Order = 1
        };
        var branch = new CourseBranch
        {
            Id = Ulid.NewUlid(),
            Block = block,
            BlockId = block.Id,
            Title = "Branch 1",
            Order = 1
        };
        var theme = new CourseTheme
        {
            Id = Ulid.NewUlid(),
            Branch = branch,
            BranchId = branch.Id,
            Key = "finger-warmup",
            Title = "Finger warmup",
            LessonContent = "Warm up each finger separately.",
            HomeworkContent = "Repeat the warmup at home.",
            Order = 1,
            ExperiencePointsReward = 1
        };

        course.Blocks.Add(block);
        block.Branches.Add(branch);
        branch.Themes.Add(theme);

        var enrollment = new CourseEnrollment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            ClientId = client.Id,
            Course = course,
            CourseId = course.Id,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            EarnedExperiencePoints = 0
        };

        await db.Courses.AddAsync(course, TestContext.Current.CancellationToken);
        await db.CourseEnrollments.AddAsync(enrollment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var startDate = new DateTime(2026, 06, 03, 12, 0, 0, DateTimeKind.Utc);
        var createResponse = await App.Client.PostAsJsonAsync(
            "/appointments",
            new
            {
                clientId = client.Id,
                serviceId = service.Id,
                courseThemeId = theme.Id,
                lessonNotes = "Разобрали разминку и посадку.",
                startDate,
                timezone = "UTC"
            },
            TestContext.Current.CancellationToken);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var createPayload = await createResponse.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken);
        createPayload.ShouldNotBeNull();

        db.ChangeTracker.Clear();

        var appointment = await db.Appointments
            .AsNoTracking()
            .FirstAsync(item => item.Id == createPayload.Id, TestContext.Current.CancellationToken);

        appointment.CourseThemeId.ShouldBe(theme.Id);
        appointment.LessonNotes.ShouldBe("Разобрали разминку и посадку.");

        var listResponse = await App.Client.GetAsync(
            $"/appointments?timezone=UTC&startDate={startDate.AddDays(-1):O}&endDate={startDate.AddDays(1):O}",
            TestContext.Current.CancellationToken);

        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listPayload = await listResponse.Content.ReadFromJsonAsync<GetAppointmentsResponse>(cancellationToken: TestContext.Current.CancellationToken);
        listPayload.ShouldNotBeNull();

        var linkedAppointment = listPayload.Appointments.Single(item => item.Id == createPayload.Id);
        linkedAppointment.CourseTheme.ShouldNotBeNull();
        linkedAppointment.CourseTheme.Title.ShouldBe("Finger warmup");
        linkedAppointment.LessonNotes.ShouldBe("Разобрали разминку и посадку.");
    }
}
