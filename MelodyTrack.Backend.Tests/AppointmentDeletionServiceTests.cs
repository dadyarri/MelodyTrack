using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
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
    public async Task EnsureAppointmentsGeneratedAsync_SkipsClientVacationDates()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();
        var rule = await TestDataFactory.CreateDailyRuleAsync(
            db,
            new DateTime(2025, 11, 14, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 16, 23, 59, 59, DateTimeKind.Utc),
            "Sofia",
            "Ivanova",
            "Piano",
            TestContext.Current.CancellationToken);

        await db.ClientVacations.AddAsync(new ClientVacation
        {
            Id = Ulid.NewUlid(),
            ClientId = rule.Client.Id,
            Client = rule.Client,
            StartDate = new DateOnly(2025, 11, 15),
            EndDate = new DateOnly(2025, 11, 15)
        }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await materializer.EnsureAppointmentsGeneratedAsync(
            new DateTime(2025, 11, 14, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 16, 23, 59, 59, DateTimeKind.Utc),
            TestContext.Current.CancellationToken);

        var dates = await db.Appointments
            .Where(item => item.RecurringRule != null && item.RecurringRule.Id == rule.Id)
            .Select(item => item.StartDate.Date)
            .ToListAsync(TestContext.Current.CancellationToken);

        dates.ShouldNotContain(new DateTime(2025, 11, 15));
        dates.Count.ShouldBe(2);
    }

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
    public async Task DeleteAsync_SingleOccurrence_FromMultiDayWeeklySeries_OnlyDeletesSelectedOccurrence()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAppointmentDeletionService>();

        var rule = await TestDataFactory.CreateWeeklyRuleAsync(
            db,
            new DateTime(2025, 11, 17, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 30, 23, 59, 59, DateTimeKind.Utc),
            1 + 4,
            "Artem",
            "Volkov",
            "Drums",
            TestContext.Current.CancellationToken);
        var startUtc = new DateTime(2025, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 11, 30, 23, 59, 59, DateTimeKind.Utc);

        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var wednesdayOccurrence = await db.Appointments
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == new DateTime(2025, 11, 19, 12, 0, 0, DateTimeKind.Utc))
            .FirstAsync(TestContext.Current.CancellationToken);

        var result = await deletionService.DeleteAsync(
            wednesdayOccurrence.Id,
            AppointmentDeleteScope.Single,
            TestContext.Current.CancellationToken);

        result.ShouldBe(DeleteAppointmentResult.Success);

        db.ChangeTracker.Clear();
        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var activeWednesdayOccurrences = await db.Appointments
            .CountAsync(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                !item.IsDeleted &&
                item.StartDate.DayOfWeek == DayOfWeek.Wednesday,
                TestContext.Current.CancellationToken);

        var deletedWednesdayOccurrences = await db.Appointments
            .CountAsync(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.IsDeleted &&
                item.StartDate.DayOfWeek == DayOfWeek.Wednesday,
                TestContext.Current.CancellationToken);

        var activeMondayOccurrences = await db.Appointments
            .CountAsync(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                !item.IsDeleted &&
                item.StartDate.DayOfWeek == DayOfWeek.Monday,
                TestContext.Current.CancellationToken);

        var updatedRule = await db.RecurrenceRules
            .Where(item => item.Id == rule.Id)
            .FirstAsync(TestContext.Current.CancellationToken);

        updatedRule.RecurrencePattern.ShouldBe(1 + 4);
        activeWednesdayOccurrences.ShouldBe(1);
        deletedWednesdayOccurrences.ShouldBe(1);
        activeMondayOccurrences.ShouldBe(2);
    }

    [Fact]
    public async Task DeleteAsync_SingleOccurrence_FromMultiDayWeeklySeries_WorksInFreshContext()
    {
        await using var arrangeScope = App.Services.CreateAsyncScope();
        var arrangeDb = arrangeScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = arrangeScope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();

        var rule = await TestDataFactory.CreateWeeklyRuleAsync(
            arrangeDb,
            new DateTime(2025, 11, 17, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 30, 23, 59, 59, DateTimeKind.Utc),
            1 + 4,
            "Artem",
            "Volkov",
            "Drums",
            TestContext.Current.CancellationToken);
        var startUtc = new DateTime(2025, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 11, 30, 23, 59, 59, DateTimeKind.Utc);

        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var wednesdayOccurrenceId = await arrangeDb.Appointments
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == new DateTime(2025, 11, 19, 12, 0, 0, DateTimeKind.Utc))
            .Select(item => item.Id)
            .FirstAsync(TestContext.Current.CancellationToken);

        await using var deleteScope = App.Services.CreateAsyncScope();
        var deletionService = deleteScope.ServiceProvider.GetRequiredService<IAppointmentDeletionService>();

        var result = await deletionService.DeleteAsync(
            wednesdayOccurrenceId,
            AppointmentDeleteScope.Single,
            TestContext.Current.CancellationToken);

        result.ShouldBe(DeleteAppointmentResult.Success);
    }

    [Fact]
    public async Task DeleteAsync_WeekdayAll_FromMultiDayWeeklySeries_DeletesOnlySelectedWeekday()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAppointmentDeletionService>();

        var rule = await TestDataFactory.CreateWeeklyRuleAsync(
            db,
            new DateTime(2025, 11, 17, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 10, 23, 59, 59, DateTimeKind.Utc),
            1 + 4,
            "Artem",
            "Volkov",
            "Drums",
            TestContext.Current.CancellationToken);
        var startUtc = new DateTime(2025, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 10, 23, 59, 59, DateTimeKind.Utc);

        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var wednesdayOccurrence = await db.Appointments
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == new DateTime(2025, 11, 19, 12, 0, 0, DateTimeKind.Utc))
            .FirstAsync(TestContext.Current.CancellationToken);

        var result = await deletionService.DeleteAsync(
            wednesdayOccurrence.Id,
            AppointmentDeleteScope.WeekdayAll,
            TestContext.Current.CancellationToken);

        result.ShouldBe(DeleteAppointmentResult.Success);

        db.ChangeTracker.Clear();
        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var updatedRule = await db.RecurrenceRules
            .Where(item => item.Id == rule.Id)
            .FirstAsync(TestContext.Current.CancellationToken);

        var activeWednesdayOccurrences = await db.Appointments
            .CountAsync(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                !item.IsDeleted &&
                item.StartDate.DayOfWeek == DayOfWeek.Wednesday,
                TestContext.Current.CancellationToken);

        var activeMondayOccurrences = await db.Appointments
            .CountAsync(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                !item.IsDeleted &&
                item.StartDate.DayOfWeek == DayOfWeek.Monday,
                TestContext.Current.CancellationToken);

        updatedRule.RecurrencePattern.ShouldBe(1);
        activeWednesdayOccurrences.ShouldBe(0);
        activeMondayOccurrences.ShouldBe(4);
    }

    [Fact]
    public async Task DeleteAsync_WeekdayThisAndFollowing_FromMultiDayWeeklySeries_KeepsEarlierSelectedWeekdayHistory()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAppointmentDeletionService>();

        var rule = await TestDataFactory.CreateWeeklyRuleAsync(
            db,
            new DateTime(2025, 11, 17, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 10, 23, 59, 59, DateTimeKind.Utc),
            1 + 4,
            "Artem",
            "Volkov",
            "Drums",
            TestContext.Current.CancellationToken);
        var startUtc = new DateTime(2025, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 12, 10, 23, 59, 59, DateTimeKind.Utc);

        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var selectedOccurrence = await db.Appointments
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == new DateTime(2025, 11, 26, 12, 0, 0, DateTimeKind.Utc))
            .FirstAsync(TestContext.Current.CancellationToken);

        var result = await deletionService.DeleteAsync(
            selectedOccurrence.Id,
            AppointmentDeleteScope.WeekdayThisAndFollowing,
            TestContext.Current.CancellationToken);

        result.ShouldBe(DeleteAppointmentResult.Success);

        db.ChangeTracker.Clear();
        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var activeMondayStarts = await db.Appointments
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                !item.IsDeleted &&
                item.StartDate.DayOfWeek == DayOfWeek.Monday)
            .OrderBy(item => item.StartDate)
            .Select(item => item.StartDate)
            .ToListAsync(TestContext.Current.CancellationToken);

        var currentRule = await db.RecurrenceRules
            .Where(item => item.Id == rule.Id)
            .FirstAsync(TestContext.Current.CancellationToken);

        var historicalWednesdayRule = await db.RecurrenceRules
            .Where(item => item.Id != rule.Id && item.RecurrencePattern == 4)
            .FirstOrDefaultAsync(TestContext.Current.CancellationToken);

        currentRule.RecurrencePattern.ShouldBe(1);
        historicalWednesdayRule.ShouldNotBeNull();
        historicalWednesdayRule.EndDate.ShouldBe(new DateTime(2025, 11, 25, 0, 0, 0, DateTimeKind.Utc));

        var activeWednesdayStarts = await db.Appointments
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == historicalWednesdayRule.Id &&
                !item.IsDeleted &&
                item.StartDate.DayOfWeek == DayOfWeek.Wednesday)
            .OrderBy(item => item.StartDate)
            .Select(item => item.StartDate)
            .ToListAsync(TestContext.Current.CancellationToken);

        activeWednesdayStarts.ShouldBe([
            new DateTime(2025, 11, 19, 12, 0, 0, DateTimeKind.Utc)
        ]);
        activeMondayStarts.ShouldBe([
            new DateTime(2025, 11, 17, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 24, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 1, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 8, 12, 0, 0, DateTimeKind.Utc)
        ]);
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

    [Fact]
    public async Task DeleteAsync_All_AfterSingleDeletedOccurrenceInMultiDaySeries_DoesNotFailOnRuleRemoval()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAppointmentDeletionService>();

        var rule = await TestDataFactory.CreateWeeklyRuleAsync(
            db,
            new DateTime(2025, 11, 17, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 30, 23, 59, 59, DateTimeKind.Utc),
            1 + 4,
            "Artem",
            "Volkov",
            "Drums",
            TestContext.Current.CancellationToken);
        var startUtc = new DateTime(2025, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 11, 23, 23, 59, 59, DateTimeKind.Utc);

        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var wednesdayOccurrence = await db.Appointments
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == new DateTime(2025, 11, 19, 12, 0, 0, DateTimeKind.Utc))
            .FirstAsync(TestContext.Current.CancellationToken);

        var singleDeleteResult = await deletionService.DeleteAsync(
            wednesdayOccurrence.Id,
            AppointmentDeleteScope.Single,
            TestContext.Current.CancellationToken);

        singleDeleteResult.ShouldBe(DeleteAppointmentResult.Success);

        var mondayOccurrence = await db.Appointments
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == new DateTime(2025, 11, 17, 12, 0, 0, DateTimeKind.Utc) &&
                !item.IsDeleted)
            .FirstAsync(TestContext.Current.CancellationToken);

        var deleteAllResult = await deletionService.DeleteAsync(
            mondayOccurrence.Id,
            AppointmentDeleteScope.All,
            TestContext.Current.CancellationToken);

        deleteAllResult.ShouldBe(DeleteAppointmentResult.Success);

        db.ChangeTracker.Clear();
        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var ruleExists = await db.RecurrenceRules
            .AnyAsync(item => item.Id == rule.Id, TestContext.Current.CancellationToken);
        var anyAppointmentsStillLinked = await db.Appointments
            .AnyAsync(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id,
                TestContext.Current.CancellationToken);

        ruleExists.ShouldBeFalse();
        anyAppointmentsStillLinked.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteAsync_ThisAndFollowing_FromSeriesStartAfterSingleDeletedOccurrenceInMultiDaySeries_DoesNotFailOnRuleRemoval()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAppointmentDeletionService>();

        var rule = await TestDataFactory.CreateWeeklyRuleAsync(
            db,
            new DateTime(2025, 11, 17, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 30, 23, 59, 59, DateTimeKind.Utc),
            1 + 4,
            "Artem",
            "Volkov",
            "Drums",
            TestContext.Current.CancellationToken);
        var startUtc = new DateTime(2025, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 11, 23, 23, 59, 59, DateTimeKind.Utc);

        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var wednesdayOccurrence = await db.Appointments
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == new DateTime(2025, 11, 19, 12, 0, 0, DateTimeKind.Utc))
            .FirstAsync(TestContext.Current.CancellationToken);

        var singleDeleteResult = await deletionService.DeleteAsync(
            wednesdayOccurrence.Id,
            AppointmentDeleteScope.Single,
            TestContext.Current.CancellationToken);

        singleDeleteResult.ShouldBe(DeleteAppointmentResult.Success);

        var mondayOccurrence = await db.Appointments
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == new DateTime(2025, 11, 17, 12, 0, 0, DateTimeKind.Utc) &&
                !item.IsDeleted)
            .FirstAsync(TestContext.Current.CancellationToken);

        var deleteFollowingResult = await deletionService.DeleteAsync(
            mondayOccurrence.Id,
            AppointmentDeleteScope.ThisAndFollowing,
            TestContext.Current.CancellationToken);

        deleteFollowingResult.ShouldBe(DeleteAppointmentResult.Success);

        db.ChangeTracker.Clear();
        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var ruleExists = await db.RecurrenceRules
            .AnyAsync(item => item.Id == rule.Id, TestContext.Current.CancellationToken);
        var anyAppointmentsStillLinked = await db.Appointments
            .AnyAsync(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id,
                TestContext.Current.CancellationToken);

        ruleExists.ShouldBeFalse();
        anyAppointmentsStillLinked.ShouldBeFalse();
    }
}
