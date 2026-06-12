using System.Net;
using System.Net.Http.Headers;
using MelodyTrack.Backend.Data.Models;
using System.Net.Http.Json;
using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.CourseEnrollments.Responses;
using MelodyTrack.Backend.Api.Courses.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class CourseProgressEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task CreateCourse_ReturnsForbiddenForRegularUser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.PostAsJsonAsync(
            "/courses",
            new
            {
                name = "Restricted course",
                blocks = new object[]
                {
                    new
                    {
                        title = "Only block",
                        order = 1,
                        branches = new object[]
                        {
                            new
                            {
                                title = "Only branch",
                                order = 1,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "intro",
                                        title = "Intro",
                                        order = 1,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 0,
                                        experiencePointsReward = 0,
                                        dependencyKeys = Array.Empty<string>()
                                    }
                                }
                            }
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CreateCourse_AllowsEmptyCourseWithoutBlocks()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.PostAsJsonAsync(
            "/courses",
            new
            {
                name = "Empty shell",
                description = "Created before structure"
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var payload = await response.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();

        db.ChangeTracker.Clear();

        var course = await db.Courses
            .AsNoTracking()
            .Include(item => item.Blocks)
            .FirstAsync(item => item.Id == payload.Id, TestContext.Current.CancellationToken);

        course.Name.ShouldBe("Empty shell");
        course.Blocks.ShouldBeEmpty();

        var auditLog = await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstAsync(item => item.Action == "course_created" && item.EntityId == payload.Id.ToString(), TestContext.Current.CancellationToken);
        auditLog.Details.ShouldBe("Курс: Empty shell; Блоков: 0; Тем: 0");
    }

    [Fact]
    public async Task CreateCourse_CreatesNestedStructureAndDependencies()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.PostAsJsonAsync(
            "/courses",
            new
            {
                name = "Piano basics",
                description = "Starter path",
                blocks = new object[]
                {
                    new
                    {
                        title = "Foundations",
                        description = "Core techniques",
                        order = 1,
                        branches = new object[]
                        {
                            new
                            {
                                title = "Right hand",
                                description = "Primary branch",
                                order = 1,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "right-intro",
                                        title = "Intro to posture",
                                        order = 1,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 5,
                                        dependencyKeys = Array.Empty<string>()
                                    },
                                    new
                                    {
                                        key = "right-scales",
                                        title = "First scales",
                                        order = 2,
                                        unlockCostPoints = 2,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 8,
                                        dependencyKeys = new[] { "left-intro" }
                                    }
                                }
                            },
                            new
                            {
                                title = "Left hand",
                                description = "Support branch",
                                order = 2,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "left-intro",
                                        title = "Left hand intro",
                                        order = 1,
                                        unlockCostPoints = 1,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 5,
                                        dependencyKeys = Array.Empty<string>()
                                    }
                                }
                            }
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var payload = await response.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();

        db.ChangeTracker.Clear();

        var course = await db.Courses
            .AsNoTracking()
            .Include(item => item.Blocks)
                .ThenInclude(block => block.Branches)
                    .ThenInclude(branch => branch.Themes)
                        .ThenInclude(theme => theme.Dependencies)
                            .ThenInclude(dependency => dependency.DependsOnTheme)
            .FirstAsync(item => item.Id == payload.Id, TestContext.Current.CancellationToken);

        course.Name.ShouldBe("Piano basics");
        course.Blocks.Count.ShouldBe(1);
        var themes = course.Blocks.SelectMany(block => block.Branches).SelectMany(branch => branch.Themes).ToList();
        themes.Count.ShouldBe(3);

        var scalesTheme = themes.Single(theme => theme.Title == "First scales");
        scalesTheme.Dependencies.Count.ShouldBe(1);
        scalesTheme.Dependencies.Single().DependsOnTheme.Title.ShouldBe("Left hand intro");

        var auditLog = await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstAsync(item => item.Action == "course_created" && item.EntityId == payload.Id.ToString(), TestContext.Current.CancellationToken);
        auditLog.Details.ShouldBe("Курс: Piano basics; Блоков: 1; Тем: 3");
    }

    [Fact]
    public async Task CreateCourse_ReturnsBadRequestForDuplicateThemeKeys()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.PostAsJsonAsync(
            "/courses",
            new
            {
                name = "Broken course",
                blocks = new object[]
                {
                    new
                    {
                        title = "Block",
                        order = 1,
                        branches = new object[]
                        {
                            new
                            {
                                title = "Branch",
                                order = 1,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "duplicate-key",
                                        title = "First theme",
                                        order = 1,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 0,
                                        experiencePointsReward = 0,
                                        dependencyKeys = Array.Empty<string>()
                                    },
                                    new
                                    {
                                        key = "duplicate-key",
                                        title = "Second theme",
                                        order = 2,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 0,
                                        experiencePointsReward = 0,
                                        dependencyKeys = Array.Empty<string>()
                                    }
                                }
                            }
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var payload = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.Errors.ShouldContain(error =>
            error.Reason.Contains("duplicate-key", StringComparison.Ordinal));

        var courseCount = await db.Courses.CountAsync(TestContext.Current.CancellationToken);
        courseCount.ShouldBe(0);
    }

    [Fact]
    public async Task CreateCourse_ReturnsBadRequestForCyclicDependencies()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.PostAsJsonAsync(
            "/courses",
            new
            {
                name = "Cycle course",
                blocks = new object[]
                {
                    new
                    {
                        title = "Block",
                        order = 1,
                        branches = new object[]
                        {
                            new
                            {
                                title = "Branch",
                                order = 1,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "theme-a",
                                        title = "Theme A",
                                        order = 1,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 0,
                                        experiencePointsReward = 0,
                                        dependencyKeys = new[] { "theme-b" }
                                    },
                                    new
                                    {
                                        key = "theme-b",
                                        title = "Theme B",
                                        order = 2,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 0,
                                        experiencePointsReward = 0,
                                        dependencyKeys = new[] { "theme-a" }
                                    }
                                }
                            }
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var payload = await response.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.Errors.ShouldContain(error =>
            error.Reason.Contains("циклическая зависимость", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CreateCourseEnrollment_InitializesThemeProgressAndReturnsThroughQuery()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Student", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var createResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId
            },
            TestContext.Current.CancellationToken);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var createPayload = await createResponse.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken);
        createPayload.ShouldNotBeNull();

        db.ChangeTracker.Clear();

        var enrollment = await db.CourseEnrollments
            .AsNoTracking()
            .Include(item => item.Themes)
                .ThenInclude(theme => theme.CourseTheme)
            .FirstAsync(item => item.Id == createPayload.Id, TestContext.Current.CancellationToken);

        enrollment.Themes.Count.ShouldBe(3);
        enrollment.Themes.Single(theme => theme.CourseTheme.Title == "Intro to rhythm").State.ShouldBe(CourseThemeProgressState.Unlocked);
        enrollment.Themes.Single(theme => theme.CourseTheme.Title == "Clap patterns").State.ShouldBe(CourseThemeProgressState.BlockedByDependency);
        enrollment.Themes.Single(theme => theme.CourseTheme.Title == "Count aloud").State.ShouldBe(CourseThemeProgressState.AvailableToUnlock);

        var listResponse = await App.Client.GetAsync($"/course-enrollments?clientId={client.Id}", TestContext.Current.CancellationToken);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listPayload = await listResponse.Content.ReadFromJsonAsync<GetCourseEnrollmentsResponse>(cancellationToken: TestContext.Current.CancellationToken);
        listPayload.ShouldNotBeNull();
        listPayload.Enrollments.Count.ShouldBe(1);
        listPayload.Enrollments.Single().Themes.Count.ShouldBe(3);
    }

    [Fact]
    public async Task CreateCourseEnrollment_ReturnsConflictWhenClientAlreadyEnrolled()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Mila", "Pupil", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var firstResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId
            },
            TestContext.Current.CancellationToken);

        firstResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var secondResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId
            },
            TestContext.Current.CancellationToken);

        secondResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var payload = await secondResponse.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.Errors.ShouldContain(error =>
            error.Reason.Contains("Клиент уже записан на этот курс.", StringComparison.Ordinal));

        var enrollmentCount = await db.CourseEnrollments
            .CountAsync(item => item.ClientId == client.Id && item.CourseId == courseId, TestContext.Current.CancellationToken);
        enrollmentCount.ShouldBe(1);
    }

    [Fact]
    public async Task DeleteCourseEnrollment_RemovesEnrollment()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Nina", "Student", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var createResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId
            },
            TestContext.Current.CancellationToken);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var createPayload = await createResponse.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken);
        createPayload.ShouldNotBeNull();

        var deleteResponse = await App.Client.DeleteAsync($"/course-enrollments/{createPayload.Id}", TestContext.Current.CancellationToken);
        deleteResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();

        var remainingCount = await db.CourseEnrollments.CountAsync(item => item.ClientId == client.Id, TestContext.Current.CancellationToken);
        remainingCount.ShouldBe(0);
    }

    [Fact]
    public async Task GetCourseEnrollments_ReturnsThemeContentAndRecentLinkedAppointments()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Lina", "Rhythm", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Rhythm class", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var createEnrollmentResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId
            },
            TestContext.Current.CancellationToken);

        createEnrollmentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var theme = await db.CourseThemes
            .AsNoTracking()
            .FirstAsync(item => item.Title == "Intro to rhythm", TestContext.Current.CancellationToken);

        await db.Appointments.AddAsync(new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            CourseThemeId = theme.Id,
            LessonNotes = "Повторили базовый ритм и хлопки.",
            StartDate = new DateTime(2026, 06, 01, 12, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 06, 01, 13, 0, 0, DateTimeKind.Utc),
            Status = AppointmentStatus.Completed,
            IsDeleted = false
        }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var listResponse = await App.Client.GetAsync($"/course-enrollments?clientId={client.Id}", TestContext.Current.CancellationToken);
        listResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var listPayload = await listResponse.Content.ReadFromJsonAsync<GetCourseEnrollmentsResponse>(cancellationToken: TestContext.Current.CancellationToken);
        listPayload.ShouldNotBeNull();

        var introTheme = listPayload.Enrollments.Single().Themes.Single(item => item.ThemeTitle == "Intro to rhythm");
        introTheme.LessonContent.ShouldBe("Clap quarter notes and identify the pulse.");
        introTheme.HomeworkContent.ShouldBe("Practice with the metronome for 5 minutes.");
        introTheme.RecentAppointments.Count.ShouldBe(1);
        introTheme.RecentAppointments.Single().LessonNotes.ShouldBe("Повторили базовый ритм и хлопки.");
    }

    [Fact]
    public async Task GetCourseEnrollments_CanFilterByCourseId()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Mira", "Tempo", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var firstCourseId = await CreateCourseAsync();
        var secondCourseId = await CreateCourseAsync();

        var firstEnrollmentResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId = firstCourseId
            },
            TestContext.Current.CancellationToken);

        firstEnrollmentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var secondEnrollmentResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId = secondCourseId
            },
            TestContext.Current.CancellationToken);

        secondEnrollmentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var response = await App.Client.GetAsync(
            $"/course-enrollments?clientId={client.Id}&courseId={firstCourseId}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<GetCourseEnrollmentsResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.Enrollments.Count.ShouldBe(1);
        payload.Enrollments.Single().CourseId.ShouldBe(firstCourseId);
    }

    [Fact]
    public async Task UpdateCourseEnrollmentThemeProgress_UnlocksThemeAndSpendsPoints()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Ira", "Unlock", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var createResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId
            },
            TestContext.Current.CancellationToken);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var enrollmentId = (await createResponse.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken))!.Id;

        var enrollment = await db.CourseEnrollments
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
            .FirstAsync(item => item.Id == enrollmentId, TestContext.Current.CancellationToken);

        enrollment.EarnedEvolutionPoints = 1;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var themeId = enrollment.Themes.Single(item => item.CourseTheme.Title == "Count aloud").Id;

        var updateResponse = await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{themeId}/actions",
            new
            {
                id = themeId,
                action = "unlock"
            },
            TestContext.Current.CancellationToken);

        updateResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();

        var updatedEnrollment = await db.CourseEnrollments
            .AsNoTracking()
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
            .FirstAsync(item => item.Id == enrollmentId, TestContext.Current.CancellationToken);

        var unlockedTheme = updatedEnrollment.Themes.Single(item => item.CourseTheme.Title == "Count aloud");
        unlockedTheme.State.ShouldBe(CourseThemeProgressState.Unlocked);
        unlockedTheme.SpentEvolutionPoints.ShouldBe(1);
        unlockedTheme.UnlockedAtUtc.ShouldNotBeNull();
        updatedEnrollment.SpentEvolutionPoints.ShouldBe(1);
    }

    [Fact]
    public async Task UpdateCourseEnrollmentThemeProgress_SupportsHomeworkRetryAndDependencyUnlock()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Toma", "Progress", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var createResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId
            },
            TestContext.Current.CancellationToken);

        createResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var enrollmentId = (await createResponse.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken))!.Id;

        var enrollment = await db.CourseEnrollments
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
            .FirstAsync(item => item.Id == enrollmentId, TestContext.Current.CancellationToken);

        var introThemeId = enrollment.Themes.Single(item => item.CourseTheme.Title == "Intro to rhythm").Id;

        (await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{introThemeId}/actions",
            new { id = introThemeId, action = "send-to-homework" },
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{introThemeId}/actions",
            new { id = introThemeId, action = "return-to-progress" },
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{introThemeId}/actions",
            new { id = introThemeId, action = "send-to-homework" },
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{introThemeId}/actions",
            new { id = introThemeId, action = "pass-homework" },
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();

        enrollment = await db.CourseEnrollments
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
                    .ThenInclude(item => item.Dependencies)
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
                    .ThenInclude(item => item.Branch)
                        .ThenInclude(item => item.Themes)
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
                    .ThenInclude(item => item.Branch)
                        .ThenInclude(item => item.Block)
            .FirstAsync(item => item.Id == enrollmentId, TestContext.Current.CancellationToken);

        var introTheme = enrollment.Themes.Single(item => item.CourseTheme.Title == "Intro to rhythm");
        introTheme.State.ShouldBe(CourseThemeProgressState.Completed);
        introTheme.CompletedAtUtc.ShouldNotBeNull();
        introTheme.EarnedEvolutionPoints.ShouldBe(1);
        introTheme.EarnedExperiencePoints.ShouldBe(3);

        enrollment.EarnedEvolutionPoints.ShouldBe(1);
        enrollment.EarnedExperiencePoints.ShouldBe(3);

        var countThemeId = enrollment.Themes.Single(item => item.CourseTheme.Title == "Count aloud").Id;

        (await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{countThemeId}/actions",
            new { id = countThemeId, action = "unlock" },
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{countThemeId}/actions",
            new { id = countThemeId, action = "send-to-homework" },
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{countThemeId}/actions",
            new { id = countThemeId, action = "pass-homework" },
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();

        var finalEnrollment = await db.CourseEnrollments
            .AsNoTracking()
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
            .FirstAsync(item => item.Id == enrollmentId, TestContext.Current.CancellationToken);

        finalEnrollment.Themes.Single(item => item.CourseTheme.Title == "Clap patterns").State.ShouldBe(CourseThemeProgressState.AvailableToUnlock);
        finalEnrollment.EarnedEvolutionPoints.ShouldBe(2);
        finalEnrollment.SpentEvolutionPoints.ShouldBe(1);
        finalEnrollment.EarnedExperiencePoints.ShouldBe(7);
    }

    [Fact]
    public async Task GetCourse_ReturnsNestedStructure()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var response = await App.Client.GetAsync($"/courses/{courseId}", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<GetCourseResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.Course.Name.ShouldBe("Rhythm track");
        payload.Course.Blocks.Count.ShouldBe(1);
        payload.Course.Blocks.Single().Branches.Count.ShouldBe(2);
    }

    [Fact]
    public async Task UpdateCourse_ReplacesStructure()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var response = await App.Client.PutAsJsonAsync(
            $"/courses/{courseId}",
            new
            {
                id = courseId,
                name = "Rhythm track updated",
                description = "Updated structure",
                blocks = new object[]
                {
                    new
                    {
                        title = "Advanced basics",
                        order = 1,
                        branches = new object[]
                        {
                            new
                            {
                                title = "Groove",
                                order = 1,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "groove-intro",
                                        title = "Groove intro",
                                        order = 1,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 2,
                                        experiencePointsReward = 6,
                                        dependencyKeys = Array.Empty<string>()
                                    }
                                }
                            }
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);

        var responseBody = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.NoContent, responseBody);

        db.ChangeTracker.Clear();

        var course = await db.Courses
            .AsNoTracking()
            .Include(item => item.Blocks)
                .ThenInclude(block => block.Branches)
                    .ThenInclude(branch => branch.Themes)
            .FirstAsync(item => item.Id == courseId, TestContext.Current.CancellationToken);

        course.Name.ShouldBe("Rhythm track updated");
        course.Blocks.Count.ShouldBe(1);
        course.Blocks.Single().Title.ShouldBe("Advanced basics");
        course.Blocks.Single().Branches.Single().Themes.ShouldHaveSingleItem().Title.ShouldBe("Groove intro");
    }

    [Fact]
    public async Task UpdateCourse_AllowsEditingWhenEnrollmentExistsAndPreservesCompletedThemeProgress()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Edit", "Enrollment", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var createEnrollmentResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId
            },
            TestContext.Current.CancellationToken);

        createEnrollmentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var enrollmentId = (await createEnrollmentResponse.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken))!.Id;

        var enrollment = await db.CourseEnrollments
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
            .FirstAsync(item => item.Id == enrollmentId, TestContext.Current.CancellationToken);

        var introEnrollmentThemeId = enrollment.Themes.Single(item => item.CourseTheme.Key == "pulse-intro").Id;
        var introCourseThemeId = enrollment.Themes.Single(item => item.CourseTheme.Key == "pulse-intro").CourseThemeId;

        (await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{introEnrollmentThemeId}/actions",
            new { id = introEnrollmentThemeId, action = "send-to-homework" },
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{introEnrollmentThemeId}/actions",
            new { id = introEnrollmentThemeId, action = "pass-homework" },
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var updateResponse = await App.Client.PutAsJsonAsync(
            $"/courses/{courseId}",
            new
            {
                id = courseId,
                name = "Rhythm track edited",
                description = "Still safe for active students",
                blocks = new object[]
                {
                    new
                    {
                        title = "Basics refreshed",
                        order = 1,
                        branches = new object[]
                        {
                            new
                            {
                                title = "Pulse",
                                order = 1,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "pulse-intro",
                                        title = "Intro to rhythm updated",
                                        lessonContent = "Updated lesson notes.",
                                        homeworkContent = "Updated homework.",
                                        order = 1,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 2,
                                        experiencePointsReward = 4,
                                        dependencyKeys = Array.Empty<string>()
                                    },
                                    new
                                    {
                                        key = "pulse-clap",
                                        title = "Clap patterns updated",
                                        order = 2,
                                        unlockCostPoints = 2,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 5,
                                        dependencyKeys = new[] { "count-intro" }
                                    }
                                }
                            },
                            new
                            {
                                title = "Counting",
                                order = 2,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "count-intro",
                                        title = "Count aloud updated",
                                        order = 1,
                                        unlockCostPoints = 1,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 4,
                                        dependencyKeys = Array.Empty<string>()
                                    }
                                }
                            }
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);

        updateResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();

        var updatedEnrollment = await db.CourseEnrollments
            .AsNoTracking()
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
            .FirstAsync(item => item.Id == enrollmentId, TestContext.Current.CancellationToken);

        var updatedIntroTheme = updatedEnrollment.Themes.Single(item => item.CourseTheme.Key == "pulse-intro");
        updatedIntroTheme.CourseThemeId.ShouldBe(introCourseThemeId);
        updatedIntroTheme.State.ShouldBe(CourseThemeProgressState.Completed);
        updatedIntroTheme.CourseTheme.Title.ShouldBe("Intro to rhythm updated");
        updatedIntroTheme.EarnedEvolutionPoints.ShouldBe(1);
        updatedIntroTheme.EarnedExperiencePoints.ShouldBe(3);
    }

    [Fact]
    public async Task UpdateCourse_AddsProgressRowsForNewThemesInExistingEnrollments()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Edit", "NewTheme", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var createEnrollmentResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId
            },
            TestContext.Current.CancellationToken);

        createEnrollmentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var enrollmentId = (await createEnrollmentResponse.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken))!.Id;

        var enrollment = await db.CourseEnrollments
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
            .FirstAsync(item => item.Id == enrollmentId, TestContext.Current.CancellationToken);
        var introEnrollmentThemeId = enrollment.Themes.Single(item => item.CourseTheme.Key == "pulse-intro").Id;

        (await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{introEnrollmentThemeId}/actions",
            new { id = introEnrollmentThemeId, action = "send-to-homework" },
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{introEnrollmentThemeId}/actions",
            new { id = introEnrollmentThemeId, action = "pass-homework" },
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var updateResponse = await App.Client.PutAsJsonAsync(
            $"/courses/{courseId}",
            new
            {
                id = courseId,
                name = "Rhythm track with new theme",
                description = "Existing enrollments receive progress rows",
                blocks = new object[]
                {
                    new
                    {
                        title = "Basics",
                        order = 1,
                        branches = new object[]
                        {
                            new
                            {
                                title = "Pulse",
                                order = 1,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "pulse-intro",
                                        title = "Intro to rhythm",
                                        order = 1,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 3,
                                        dependencyKeys = Array.Empty<string>()
                                    },
                                    new
                                    {
                                        key = "pulse-fill",
                                        title = "Fill the pulse",
                                        order = 2,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 3,
                                        dependencyKeys = Array.Empty<string>()
                                    },
                                    new
                                    {
                                        key = "pulse-clap",
                                        title = "Clap patterns",
                                        order = 3,
                                        unlockCostPoints = 2,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 5,
                                        dependencyKeys = new[] { "count-intro" }
                                    }
                                }
                            },
                            new
                            {
                                title = "Counting",
                                order = 2,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "count-intro",
                                        title = "Count aloud",
                                        order = 1,
                                        unlockCostPoints = 1,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 4,
                                        dependencyKeys = Array.Empty<string>()
                                    }
                                }
                            }
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);

        updateResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();

        var updatedEnrollment = await db.CourseEnrollments
            .AsNoTracking()
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
            .FirstAsync(item => item.Id == enrollmentId, TestContext.Current.CancellationToken);

        var addedTheme = updatedEnrollment.Themes.Single(item => item.CourseTheme.Key == "pulse-fill");
        addedTheme.State.ShouldBe(CourseThemeProgressState.Unlocked);
        addedTheme.UnlockedAtUtc.ShouldNotBeNull();
    }

    [Fact]
    public async Task UpdateCourseEnrollmentThemeProgress_RejectsCompletionWhenNewDependencyIsNotCompleted()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Edit", "Dependency", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var createEnrollmentResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId
            },
            TestContext.Current.CancellationToken);

        createEnrollmentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var enrollmentId = (await createEnrollmentResponse.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken))!.Id;

        var enrollment = await db.CourseEnrollments
            .Include(item => item.Themes)
                .ThenInclude(item => item.CourseTheme)
            .FirstAsync(item => item.Id == enrollmentId, TestContext.Current.CancellationToken);
        var introEnrollmentThemeId = enrollment.Themes.Single(item => item.CourseTheme.Key == "pulse-intro").Id;

        (await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{introEnrollmentThemeId}/actions",
            new { id = introEnrollmentThemeId, action = "send-to-homework" },
            TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var updateResponse = await App.Client.PutAsJsonAsync(
            $"/courses/{courseId}",
            new
            {
                id = courseId,
                name = "Rhythm track with stricter dependency",
                description = "Completion must respect edited dependencies",
                blocks = new object[]
                {
                    new
                    {
                        title = "Basics",
                        order = 1,
                        branches = new object[]
                        {
                            new
                            {
                                title = "Pulse",
                                order = 1,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "pulse-intro",
                                        title = "Intro to rhythm",
                                        order = 1,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 3,
                                        dependencyKeys = new[] { "count-intro" }
                                    },
                                    new
                                    {
                                        key = "pulse-clap",
                                        title = "Clap patterns",
                                        order = 2,
                                        unlockCostPoints = 2,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 5,
                                        dependencyKeys = new[] { "count-intro" }
                                    }
                                }
                            },
                            new
                            {
                                title = "Counting",
                                order = 2,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "count-intro",
                                        title = "Count aloud",
                                        order = 1,
                                        unlockCostPoints = 1,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 4,
                                        dependencyKeys = Array.Empty<string>()
                                    }
                                }
                            }
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);

        updateResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var passResponse = await App.Client.PostAsJsonAsync(
            $"/course-enrollment-themes/{introEnrollmentThemeId}/actions",
            new { id = introEnrollmentThemeId, action = "pass-homework" },
            TestContext.Current.CancellationToken);

        passResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var payload = await passResponse.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.Errors.ShouldContain(error =>
            error.Reason.Contains("Нельзя завершить тему, пока не выполнены предыдущие темы и зависимости.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateCourse_AllowsEditingWhenThemeIsLinkedToAppointmentAndPreservesLink()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Edit", "Appointment", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Course lesson", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var theme = await db.CourseThemes
            .FirstAsync(item => item.Key == "pulse-intro", TestContext.Current.CancellationToken);

        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            CourseThemeId = theme.Id,
            LessonNotes = "Linked before course edit.",
            StartDate = new DateTime(2026, 06, 10, 10, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 06, 10, 11, 0, 0, DateTimeKind.Utc),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };

        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updateResponse = await App.Client.PutAsJsonAsync(
            $"/courses/{courseId}",
            new
            {
                id = courseId,
                name = "Rhythm track edited",
                description = "Appointments remain linked",
                blocks = new object[]
                {
                    new
                    {
                        title = "Basics refreshed",
                        order = 1,
                        branches = new object[]
                        {
                            new
                            {
                                title = "Pulse",
                                order = 1,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "pulse-intro",
                                        title = "Intro for linked lesson",
                                        order = 1,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 3,
                                        dependencyKeys = Array.Empty<string>()
                                    },
                                    new
                                    {
                                        key = "pulse-clap",
                                        title = "Clap patterns",
                                        order = 2,
                                        unlockCostPoints = 2,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 5,
                                        dependencyKeys = new[] { "count-intro" }
                                    }
                                }
                            },
                            new
                            {
                                title = "Counting",
                                order = 2,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "count-intro",
                                        title = "Count aloud",
                                        order = 1,
                                        unlockCostPoints = 1,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 4,
                                        dependencyKeys = Array.Empty<string>()
                                    }
                                }
                            }
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);

        updateResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();

        var updatedAppointment = await db.Appointments
            .AsNoTracking()
            .Include(item => item.CourseTheme)
            .FirstAsync(item => item.Id == appointment.Id, TestContext.Current.CancellationToken);

        updatedAppointment.CourseThemeId.ShouldBe(theme.Id);
        updatedAppointment.CourseTheme.ShouldNotBeNull();
        updatedAppointment.CourseTheme.Title.ShouldBe("Intro for linked lesson");
    }

    [Fact]
    public async Task UpdateCourse_AllowsRemovingThemeLinkedToAppointmentAndUnlinksAppointment()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Edit", "AppointmentRemoval", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Course lesson", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var theme = await db.CourseThemes
            .FirstAsync(item => item.Key == "pulse-intro", TestContext.Current.CancellationToken);

        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            CourseThemeId = theme.Id,
            LessonNotes = "Theme will be removed from course.",
            StartDate = new DateTime(2026, 06, 10, 10, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 06, 10, 11, 0, 0, DateTimeKind.Utc),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };

        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var updateResponse = await App.Client.PutAsJsonAsync(
            $"/courses/{courseId}",
            new
            {
                id = courseId,
                name = "Rhythm track edited",
                description = "Appointments survive removed themes",
                blocks = new object[]
                {
                    new
                    {
                        title = "Basics refreshed",
                        order = 1,
                        branches = new object[]
                        {
                            new
                            {
                                title = "Pulse",
                                order = 1,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "pulse-clap",
                                        title = "Clap patterns",
                                        order = 1,
                                        unlockCostPoints = 2,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 5,
                                        dependencyKeys = new[] { "count-intro" }
                                    }
                                }
                            },
                            new
                            {
                                title = "Counting",
                                order = 2,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "count-intro",
                                        title = "Count aloud",
                                        order = 1,
                                        unlockCostPoints = 1,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 4,
                                        dependencyKeys = Array.Empty<string>()
                                    }
                                }
                            }
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);

        updateResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();

        var updatedAppointment = await db.Appointments
            .AsNoTracking()
            .FirstAsync(item => item.Id == appointment.Id, TestContext.Current.CancellationToken);
        updatedAppointment.CourseThemeId.ShouldBeNull();

        var themeStillExists = await db.CourseThemes
            .AsNoTracking()
            .AnyAsync(item => item.Id == theme.Id, TestContext.Current.CancellationToken);
        themeStillExists.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateCourse_ReturnsBadRequestWhenRemovingThemeUsedInProgress()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Edit", "Removal", TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var courseId = await CreateCourseAsync();

        var createEnrollmentResponse = await App.Client.PostAsJsonAsync(
            "/course-enrollments",
            new
            {
                clientId = client.Id,
                courseId
            },
            TestContext.Current.CancellationToken);

        createEnrollmentResponse.StatusCode.ShouldBe(HttpStatusCode.Created);

        var updateResponse = await App.Client.PutAsJsonAsync(
            $"/courses/{courseId}",
            new
            {
                id = courseId,
                name = "Rhythm track broken",
                blocks = new object[]
                {
                    new
                    {
                        title = "Basics",
                        order = 1,
                        branches = new object[]
                        {
                            new
                            {
                                title = "Pulse",
                                order = 1,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "pulse-intro",
                                        title = "Intro to rhythm",
                                        order = 1,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 3,
                                        dependencyKeys = Array.Empty<string>()
                                    }
                                }
                            }
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);

        updateResponse.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        var payload = await updateResponse.Content.ReadFromJsonAsync<ProblemDetails>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.Errors.ShouldContain(error =>
            error.Reason.Contains("Нельзя удалять темы курса, которые уже участвуют в прогрессе клиентов.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetCourses_FiltersBySearch()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        await CreateCourseAsync();

        var response = await App.Client.GetAsync("/courses?search=Rhythm", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<GetCoursesResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.Courses.Count.ShouldBe(1);
        payload.Courses.Single().Name.ShouldBe("Rhythm track");
        payload.Courses.Single().ThemeCount.ShouldBe(3);
    }

    private async Task<Ulid> CreateCourseAsync()
    {
        var response = await App.Client.PostAsJsonAsync(
            "/courses",
            new
            {
                name = "Rhythm track",
                description = "Enrollment-ready course",
                blocks = new object[]
                {
                    new
                    {
                        title = "Basics",
                        order = 1,
                        branches = new object[]
                        {
                            new
                            {
                                title = "Pulse",
                                order = 1,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "pulse-intro",
                                        title = "Intro to rhythm",
                                        lessonContent = "Clap quarter notes and identify the pulse.",
                                        homeworkContent = "Practice with the metronome for 5 minutes.",
                                        order = 1,
                                        unlockCostPoints = 0,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 3,
                                        dependencyKeys = Array.Empty<string>()
                                    },
                                    new
                                    {
                                        key = "pulse-clap",
                                        title = "Clap patterns",
                                        lessonContent = "Switch between simple clap combinations.",
                                        homeworkContent = "Record three clap patterns at home.",
                                        order = 2,
                                        unlockCostPoints = 2,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 5,
                                        dependencyKeys = new[] { "count-intro" }
                                    }
                                }
                            },
                            new
                            {
                                title = "Counting",
                                order = 2,
                                themes = new object[]
                                {
                                    new
                                    {
                                        key = "count-intro",
                                        title = "Count aloud",
                                        lessonContent = "Count beats aloud while tapping.",
                                        homeworkContent = "Count four bars before starting the track.",
                                        order = 1,
                                        unlockCostPoints = 1,
                                        evolutionPointsReward = 1,
                                        experiencePointsReward = 4,
                                        dependencyKeys = Array.Empty<string>()
                                    }
                                }
                            }
                        }
                    }
                }
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        var payload = await response.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        return payload.Id;
    }
}
