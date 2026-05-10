using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class AppointmentDeletionServiceTests(RecurringAppointmentMaterializerFixture fixture)
    : IClassFixture<RecurringAppointmentMaterializerFixture>
{
    [Fact]
    public async Task DeleteAsync_SingleRecurringOccurrence_KeepsOccurrenceDeletedAfterRematerialization()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAppointmentDeletionService>();

        var rule = await CreateDailyRuleAsync(db, TestContext.Current.CancellationToken);
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
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAppointmentDeletionService>();

        var rule = await CreateDailyRuleAsync(db, TestContext.Current.CancellationToken);
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
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();
        var deletionService = scope.ServiceProvider.GetRequiredService<IAppointmentDeletionService>();

        var rule = await CreateWeeklyRuleAsync(db, TestContext.Current.CancellationToken);
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

    private static async Task<AppointmentRecurrenceRule> CreateDailyRuleAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Daily, cancellationToken);
        var client = new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = "Sofia",
            LastName = "Ivanova",
            Contacts = new ClientContacts { Id = Ulid.NewUlid() }
        };
        var service = new Service
        {
            Id = Ulid.NewUlid(),
            Name = "Piano"
        };

        var rule = new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = new DateTime(2025, 11, 14, 15, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2025, 11, 20, 23, 59, 59, DateTimeKind.Utc),
            RecurrenceType = recurrenceType,
            RecurrencePattern = 1
        };

        await db.RecurrenceRules.AddAsync(rule, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    private static async Task<AppointmentRecurrenceRule> CreateWeeklyRuleAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Weekly, cancellationToken);
        var client = new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = "Artem",
            LastName = "Volkov",
            Contacts = new ClientContacts { Id = Ulid.NewUlid() }
        };
        var service = new Service
        {
            Id = Ulid.NewUlid(),
            Name = "Drums"
        };

        var rule = new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = new DateTime(2025, 11, 17, 12, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            RecurrenceType = recurrenceType,
            RecurrencePattern = 1 + 4
        };

        await db.RecurrenceRules.AddAsync(rule, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }
}
