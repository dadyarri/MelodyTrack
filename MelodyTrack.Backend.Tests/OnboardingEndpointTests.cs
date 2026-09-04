using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Onboarding;
using MelodyTrack.Backend.Api.Onboarding.Responses;
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
public class OnboardingEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task GetState_CreatesCurrentOnboardingDefinition()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);

        using var response = await SendAuthenticatedAsync(user, HttpMethod.Get, "/onboarding");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var state = await response.Content.ReadFromJsonAsync<OnboardingStateResponse>(TestContext.Current.CancellationToken);
        state.ShouldNotBeNull();
        state.DefinitionVersion.ShouldBe(OnboardingDefaults.CurrentDefinitionVersion);
        state.Status.ShouldBe("active");
        state.CurrentStep.ShouldBe(OnboardingDefaults.InitialStep);
        state.CurrentPath.ShouldBe(OnboardingDefaults.InitialPath);
        state.ShouldLaunch.ShouldBeTrue();
    }

    [Fact]
    public async Task GetState_ReactivatesCompletedLegacyDefinition()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);

        using (var initialResponse = await SendAuthenticatedAsync(user, HttpMethod.Get, "/onboarding"))
        {
            initialResponse.EnsureSuccessStatusCode();
        }

        db.ChangeTracker.Clear();
        var persistedState = await db.UserOnboardingStates.SingleAsync(
            state => state.UserId == user.Id,
            TestContext.Current.CancellationToken);
        persistedState.DefinitionVersion = OnboardingDefaults.CurrentDefinitionVersion - 1;
        persistedState.Status = OnboardingStatus.Completed;
        persistedState.CurrentStep = "old-finish";
        persistedState.CurrentPath = "/old";
        persistedState.CompletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        using var response = await SendAuthenticatedAsync(user, HttpMethod.Get, "/onboarding");
        var state = await response.Content.ReadFromJsonAsync<OnboardingStateResponse>(TestContext.Current.CancellationToken);

        state.ShouldNotBeNull();
        state.DefinitionVersion.ShouldBe(OnboardingDefaults.CurrentDefinitionVersion);
        state.Status.ShouldBe("active");
        state.CurrentStep.ShouldBe(OnboardingDefaults.InitialStep);
        state.CurrentPath.ShouldBe(OnboardingDefaults.InitialPath);
        state.CompletedAtUtc.ShouldBeNull();
        state.ShouldLaunch.ShouldBeTrue();
    }

    [Fact]
    public async Task ProgressLifecycle_SupportsResumeSkipResetAndCompletion()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        using (var progressResponse = await SendAuthenticatedAsync(
                   user,
                   HttpMethod.Patch,
                   "/onboarding",
                   new { currentStep = "admin-schedule", currentPath = "/schedule" }))
        {
            var progress = await progressResponse.Content.ReadFromJsonAsync<OnboardingStateResponse>(TestContext.Current.CancellationToken);
            progress.ShouldNotBeNull();
            progress.Status.ShouldBe("active");
            progress.CurrentStep.ShouldBe("admin-schedule");
            progress.CurrentPath.ShouldBe("/schedule");
        }

        using (var skipResponse = await SendAuthenticatedAsync(user, HttpMethod.Post, "/onboarding/skip"))
        {
            var skipped = await skipResponse.Content.ReadFromJsonAsync<OnboardingStateResponse>(TestContext.Current.CancellationToken);
            skipped.ShouldNotBeNull();
            skipped.Status.ShouldBe("skipped");
            skipped.ShouldLaunch.ShouldBeFalse();
        }

        using (var resetResponse = await SendAuthenticatedAsync(user, HttpMethod.Delete, "/onboarding"))
        {
            var reset = await resetResponse.Content.ReadFromJsonAsync<OnboardingStateResponse>(TestContext.Current.CancellationToken);
            reset.ShouldNotBeNull();
            reset.Status.ShouldBe("active");
            reset.CurrentStep.ShouldBe(OnboardingDefaults.InitialStep);
            reset.DefinitionVersion.ShouldBe(OnboardingDefaults.CurrentDefinitionVersion);
        }

        using var completionResponse = await SendAuthenticatedAsync(user, HttpMethod.Post, "/onboarding/completion");
        var completed = await completionResponse.Content.ReadFromJsonAsync<OnboardingStateResponse>(TestContext.Current.CancellationToken);
        completed.ShouldNotBeNull();
        completed.Status.ShouldBe("completed");
        completed.CompletedAtUtc.ShouldNotBeNull();
        completed.ShouldLaunch.ShouldBeFalse();
    }

    [Theory]
    [InlineData("GET", "/onboarding")]
    [InlineData("PATCH", "/onboarding")]
    [InlineData("POST", "/onboarding/skip")]
    [InlineData("POST", "/onboarding/completion")]
    [InlineData("DELETE", "/onboarding")]
    public async Task ClientPortalUser_IsIneligibleAndDoesNotCreateState(string method, string path)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var client = await TestDataFactory.CreateClientAsync(db, "Portal", "Student", TestContext.Current.CancellationToken);
        var clientRole = await db.Roles.FirstAsync(role => role.RoleName == UserRoles.Client, TestContext.Current.CancellationToken);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            FirstName = client.FirstName,
            LastName = client.LastName,
            Email = $"client-{client.Id}@portal.test",
            Password = "hash",
            Role = clientRole,
            ClientId = client.Id
        };
        await db.Users.AddAsync(user, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var body = method == "PATCH" ? new { currentStep = "portal-step", currentPath = "/portal" } : null;
        using var response = await SendAuthenticatedAsync(user, new HttpMethod(method), path, body);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        db.UserOnboardingStates.AsNoTracking().Any(state => state.UserId == user.Id).ShouldBeFalse();
    }

    private async Task<HttpResponseMessage> SendAuthenticatedAsync(
        User user,
        HttpMethod method,
        string path,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return await App.Client.SendAsync(request, TestContext.Current.CancellationToken);
    }
}
