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
    public async Task CreateExpense_StoresSelectedDateAndKopecks()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var date = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);
        var response = await App.Client.PostAsJsonAsync(
            "/expenses",
            new CreateExpenseRequest
            {
                Description = "Sheet music",
                Amount = 199.99m,
                Date = date
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var payload = await response.Content.ReadFromJsonAsync<CreateEntityResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();

        var expense = await db.Expenses.SingleAsync(item => item.Id == payload.Id, TestContext.Current.CancellationToken);
        expense.Amount.ShouldBe(199.99m);
        expense.Date.ShouldBe(date);
    }

    [Fact]
    public async Task UpdateExpense_UpdatesEntityForSuperuser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        var expense = new Expense
        {
            Id = Ulid.NewUlid(),
            Description = "Studio rent",
            Amount = 5000m,
            Date = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc)
        };
        await db.Expenses.AddAsync(expense, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(superuser));

        var updatedDate = new DateTime(2026, 7, 2, 0, 0, 0, DateTimeKind.Utc);
        var response = await App.Client.PutAsJsonAsync(
            $"/expenses/{expense.Id}",
            new UpdateExpenseRequest
            {
                Id = expense.Id,
                Description = "Updated studio rent",
                Amount = 5000.50m,
                Date = updatedDate
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        await using var verificationScope = App.Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var updatedExpense = await verificationDb.Expenses.SingleAsync(item => item.Id == expense.Id, TestContext.Current.CancellationToken);
        updatedExpense.Description.ShouldBe("Updated studio rent");
        updatedExpense.Amount.ShouldBe(5000.50m);
        updatedExpense.Date.ShouldBe(updatedDate);
    }

    [Fact]
    public async Task UpdateExpense_ReturnsForbiddenForAdmin()
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
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.PutAsJsonAsync(
            $"/expenses/{expense.Id}",
            new UpdateExpenseRequest
            {
                Id = expense.Id,
                Description = expense.Description,
                Amount = expense.Amount,
                Date = expense.Date
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

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

    [Fact]
    public async Task GetExpenses_ReturnsForbiddenForRegularUser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.GetAsync("/expenses", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
