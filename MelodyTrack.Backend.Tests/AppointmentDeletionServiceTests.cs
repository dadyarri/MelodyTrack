using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class AppointmentDeletionServiceTests(MelodyTrackFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DeleteAsync_SingleRecurringOccurrence_KeepsOccurrenceDeletedAfterRematerialization()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAppointmentDeletionService>();

        var rule = await TestDataFactory.CreateDailyRuleAsync(
            db,
            new DateTime(2025, 11, 14, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 20, 23, 59, 59, DateTimeKind.Utc),
            "Sofia",
            "Ivanova",
            "Piano",
            TestContext.Current.CancellationToken);
        var startUtc = new DateTime(2025, 11, 14, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 11, 20, 23, 59, 59, DateTimeKind.Utc);

        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var occurrence = await db.Appointments
            .Where(item => item.RecurringRule != null && item.RecurringRule.Id == rule.Id && item.StartDate == new DateTime(2025, 11, 16, 15, 0, 0, DateTimeKind.Utc))
            .FirstAsync(TestContext.Current.CancellationToken);

        var result = await deletionService.DeleteAsync(occurrence.Id, AppointmentDeleteScope.Single, TestContext.Current.CancellationToken);

        result.ShouldBe(DeleteAppointmentResult.Success);

        db.ChangeTracker.Clear();
        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var activeOccurrences = await db.Appointments
            .CountAsync(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == occurrence.StartDate &&
                !item.IsDeleted,
                TestContext.Current.CancellationToken);

        var deletedOccurrences = await db.Appointments
            .CountAsync(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == occurrence.StartDate &&
                item.IsDeleted,
                TestContext.Current.CancellationToken);

        activeOccurrences.ShouldBe(0);
        deletedOccurrences.ShouldBe(1);
    }

    [Fact]
    public async Task DeleteAsync_ThisAndFollowing_TrimsRuleAndDeletesFollowingOccurrences()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAppointmentDeletionService>();

        var rule = await TestDataFactory.CreateDailyRuleAsync(
            db,
            new DateTime(2025, 11, 14, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 20, 23, 59, 59, DateTimeKind.Utc),
            "Sofia",
            "Ivanova",
            "Piano",
            TestContext.Current.CancellationToken);
        var startUtc = new DateTime(2025, 11, 14, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 11, 20, 23, 59, 59, DateTimeKind.Utc);

        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var occurrence = await db.Appointments
            .Where(item => item.RecurringRule != null && item.RecurringRule.Id == rule.Id && item.StartDate == new DateTime(2025, 11, 17, 15, 0, 0, DateTimeKind.Utc))
            .FirstAsync(TestContext.Current.CancellationToken);

        var result = await deletionService.DeleteAsync(occurrence.Id, AppointmentDeleteScope.ThisAndFollowing, TestContext.Current.CancellationToken);

        result.ShouldBe(DeleteAppointmentResult.Success);

        db.ChangeTracker.Clear();
        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var activeStarts = await db.Appointments
            .Where(item => item.RecurringRule != null && item.RecurringRule.Id == rule.Id && !item.IsDeleted)
            .OrderBy(item => item.StartDate)
            .Select(item => item.StartDate)
            .ToListAsync(TestContext.Current.CancellationToken);

        var updatedRule = await db.RecurrenceRules
            .Where(item => item.Id == rule.Id)
            .FirstAsync(TestContext.Current.CancellationToken);

        activeStarts.ShouldBe([
            new DateTime(2025, 11, 14, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 15, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 16, 15, 0, 0, DateTimeKind.Utc)
        ]);
        updatedRule.EndDate.ShouldBe(new DateTime(2025, 11, 16, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task DeleteAsync_All_RemovesRuleAndDeletesAllOccurrences()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAppointmentDeletionService>();

        var rule = await TestDataFactory.CreateWeeklyRuleAsync(
            db,
            new DateTime(2025, 11, 17, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            1 + 4,
            "Artem",
            "Volkov",
            "Drums",
            TestContext.Current.CancellationToken);
        var startUtc = new DateTime(2025, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 11, 23, 23, 59, 59, DateTimeKind.Utc);

        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var occurrence = await db.Appointments
            .Where(item => item.RecurringRule != null && item.RecurringRule.Id == rule.Id)
            .OrderBy(item => item.StartDate)
            .FirstAsync(TestContext.Current.CancellationToken);

        var result = await deletionService.DeleteAsync(occurrence.Id, AppointmentDeleteScope.All, TestContext.Current.CancellationToken);

        result.ShouldBe(DeleteAppointmentResult.Success);

        db.ChangeTracker.Clear();
        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var ruleExists = await db.RecurrenceRules
            .AnyAsync(item => item.Id == rule.Id, TestContext.Current.CancellationToken);

        var activeOccurrences = await db.Appointments
            .CountAsync(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                !item.IsDeleted,
                TestContext.Current.CancellationToken);

        ruleExists.ShouldBeFalse();
        activeOccurrences.ShouldBe(0);
    }
}
