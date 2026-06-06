using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.ClientSources.Endpoints;
using MelodyTrack.Backend.Api.ClientSources.Responses;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.ExpenseCategories.Endpoints;
using MelodyTrack.Backend.Api.ExpenseCategories.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class ReferenceBookEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task GetExpenseCategories_ReturnsForbiddenForRegularUser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (response, _) = await App.Client.GETAsync<GetExpenseCategoriesEndpoint, EmptyRequest, GetExpenseCategoriesResponse>(
            EmptyRequest.Instance);

        App.Client.DefaultRequestHeaders.Authorization = null;

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetExpenseCategories_ReturnsLastActivity()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var category = new ExpenseCategory
        {
            Id = Ulid.NewUlid(),
            Name = "Rent"
        };

        await db.ExpenseCategories.AddAsync(category, TestContext.Current.CancellationToken);

        var activityId = Ulid.NewUlid();
        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                Id = activityId,
                CreatedAtUtc = DateTime.UtcNow,
                Category = "expense_category",
                Action = "expense_category_created",
                EntityType = "expense_category",
                EntityId = category.Id.ToString(),
                Details = "Category created"
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (response, payload) = await App.Client.GETAsync<GetExpenseCategoriesEndpoint, EmptyRequest, GetExpenseCategoriesResponse>(
            EmptyRequest.Instance);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        payload.ShouldNotBeNull();
        var item = payload.Categories.Single(entry => entry.Id == category.Id);
        var lastActivity = item.LastActivity;
        lastActivity.ShouldNotBeNull();
        lastActivity.Id.ShouldBe(activityId);
    }

    [Fact]
    public async Task DeleteExpenseCategory_ReturnsConflictWhenExpectedActivityIdIsStale()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var category = new ExpenseCategory
        {
            Id = Ulid.NewUlid(),
            Name = "Utilities"
        };

        await db.ExpenseCategories.AddAsync(category, TestContext.Current.CancellationToken);

        var latestActivityId = Ulid.NewUlid();
        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                Id = latestActivityId,
                CreatedAtUtc = DateTime.UtcNow,
                Category = "expense_category",
                Action = "expense_category_created",
                EntityType = "expense_category",
                EntityId = category.Id.ToString(),
                Details = "Category created"
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.DeleteAsync(
            $"/expense-categories/{category.Id}?expectedActivityId={Ulid.NewUlid()}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var payload = await response.Content.ReadFromJsonAsync<StaleEntityConflictResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.EntityType.ShouldBe("expense_category");
        payload.CurrentActivity.ShouldNotBeNull();
        payload.CurrentActivity.Id.ShouldBe(latestActivityId);
    }

    [Fact]
    public async Task GetClientSources_ReturnsLastActivity()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var source = new ClientSource
        {
            Id = Ulid.NewUlid(),
            Name = "Instagram"
        };

        await db.ClientSources.AddAsync(source, TestContext.Current.CancellationToken);

        var activityId = Ulid.NewUlid();
        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                Id = activityId,
                CreatedAtUtc = DateTime.UtcNow,
                Category = "clients",
                Action = "client_source_created",
                EntityType = "client_source",
                EntityId = source.Id.ToString(),
                Details = "Source created"
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (response, payload) = await App.Client.GETAsync<GetClientSourcesEndpoint, EmptyRequest, GetClientSourcesResponse>(
            EmptyRequest.Instance);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        payload.ShouldNotBeNull();
        var item = payload.Sources.Single(entry => entry.Id == source.Id);
        var lastActivity = item.LastActivity;
        lastActivity.ShouldNotBeNull();
        lastActivity.Id.ShouldBe(activityId);
    }

    [Fact]
    public async Task GetClientSources_ReturnsForbiddenForRegularUser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (response, _) = await App.Client.GETAsync<GetClientSourcesEndpoint, EmptyRequest, GetClientSourcesResponse>(
            EmptyRequest.Instance);

        App.Client.DefaultRequestHeaders.Authorization = null;

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteClientSource_ReturnsConflictWhenExpectedActivityIdIsStale()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var source = new ClientSource
        {
            Id = Ulid.NewUlid(),
            Name = "Referral"
        };

        await db.ClientSources.AddAsync(source, TestContext.Current.CancellationToken);

        var latestActivityId = Ulid.NewUlid();
        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                Id = latestActivityId,
                CreatedAtUtc = DateTime.UtcNow,
                Category = "clients",
                Action = "client_source_created",
                EntityType = "client_source",
                EntityId = source.Id.ToString(),
                Details = "Source created"
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.DeleteAsync(
            $"/client-sources/{source.Id}?expectedActivityId={Ulid.NewUlid()}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var payload = await response.Content.ReadFromJsonAsync<StaleEntityConflictResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.EntityType.ShouldBe("client_source");
        payload.CurrentActivity.ShouldNotBeNull();
        payload.CurrentActivity.Id.ShouldBe(latestActivityId);
    }
}
