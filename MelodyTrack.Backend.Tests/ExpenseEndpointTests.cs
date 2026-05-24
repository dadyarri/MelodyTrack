using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Expenses.Endpoints;
using MelodyTrack.Backend.Api.Expenses.Requests;
using MelodyTrack.Backend.Api.Expenses.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class ExpenseEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task GetExpenses_ReturnsLastActivity()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var expense = new Expense
        {
            Id = Ulid.NewUlid(),
            Description = "Studio rent",
            Amount = 5000m,
            Date = DateTime.UtcNow
        };

        await db.Expenses.AddAsync(expense, TestContext.Current.CancellationToken);

        var activityId = Ulid.NewUlid();
        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                Id = activityId,
                CreatedAtUtc = DateTime.UtcNow,
                Category = "expenses",
                Action = "expense_created",
                EntityType = "expense",
                EntityId = expense.Id.ToString(),
                Details = "Expense created"
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (response, payload) = await App.Client.GETAsync<GetExpensesEndpoint, GetExpensesPaginatedRequest, GetExpensesResponse>(
            new GetExpensesPaginatedRequest
            {
                Page = 1,
                PageSize = 10
            });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        payload.ShouldNotBeNull();
        payload.Data.Count.ShouldBe(1);
        var lastActivity = payload.Data[0].LastActivity;
        lastActivity.ShouldNotBeNull();
        lastActivity.Id.ShouldBe(activityId);
    }

    [Fact]
    public async Task DeleteExpense_ReturnsConflictWhenExpectedActivityIdIsStale()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var expense = new Expense
        {
            Id = Ulid.NewUlid(),
            Description = "Utilities",
            Amount = 1200m,
            Date = DateTime.UtcNow
        };

        await db.Expenses.AddAsync(expense, TestContext.Current.CancellationToken);

        var latestActivityId = Ulid.NewUlid();
        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                Id = latestActivityId,
                CreatedAtUtc = DateTime.UtcNow,
                Category = "expenses",
                Action = "expense_created",
                EntityType = "expense",
                EntityId = expense.Id.ToString(),
                Details = "Expense created"
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.DeleteAsync(
            $"/expenses/{expense.Id}?expectedActivityId={Ulid.NewUlid()}",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var payload = await response.Content.ReadFromJsonAsync<StaleEntityConflictResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.EntityType.ShouldBe("expense");
        payload.CurrentActivity.ShouldNotBeNull();
        payload.CurrentActivity.Id.ShouldBe(latestActivityId);

        var exists = await db.Expenses.AnyAsync(item => item.Id == expense.Id, TestContext.Current.CancellationToken);
        exists.ShouldBeTrue();
    }
}
