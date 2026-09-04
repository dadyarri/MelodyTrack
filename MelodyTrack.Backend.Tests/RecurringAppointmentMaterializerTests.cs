using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class RecurringAppointmentMaterializerTests(MelodyTrackFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsureAppointmentsGeneratedAsync_RepeatedForSameWeek_DoesNotCreateDuplicates()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();

        var rule = await TestDataFactory.CreateWeeklyRuleAsync(
            db,
            new DateTime(2025, 11, 10, 12, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            1 + 4,
            "Ivan",
            "Petrov",
            "Vocal",
            TestContext.Current.CancellationToken);
        var startUtc = new DateTime(2025, 11, 17, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 11, 23, 23, 59, 59, DateTimeKind.Utc);

        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);
        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var appointments = await db.Appointments
            .Where(appointment => appointment.RecurringRule != null && appointment.RecurringRule.Id == rule.Id)
            .OrderBy(appointment => appointment.StartDate)
            .ToListAsync(TestContext.Current.CancellationToken);

        appointments.Count.ShouldBe(2);
        appointments.Select(appointment => appointment.StartDate).Distinct().Count().ShouldBe(2);
        appointments.All(appointment => appointment.Id != Ulid.Empty).ShouldBeTrue();
    }

    [Fact]
    public async Task EnsureAppointmentsGeneratedAsync_WithOverlappingRanges_DoesNotCreateDuplicates()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();

        var rule = await TestDataFactory.CreateDailyRuleAsync(
            db,
            new DateTime(2025, 11, 14, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 20, 23, 59, 59, DateTimeKind.Utc),
            "Maria",
            "Sokolova",
            "Guitar",
            TestContext.Current.CancellationToken);
        var firstStartUtc = new DateTime(2025, 11, 14, 0, 0, 0, DateTimeKind.Utc);
        var firstEndUtc = new DateTime(2025, 11, 20, 23, 59, 59, DateTimeKind.Utc);
        var secondStartUtc = new DateTime(2025, 11, 16, 0, 0, 0, DateTimeKind.Utc);
        var secondEndUtc = new DateTime(2025, 11, 22, 23, 59, 59, DateTimeKind.Utc);

        await materializer.EnsureAppointmentsGeneratedAsync(firstStartUtc, firstEndUtc, TestContext.Current.CancellationToken);
        await materializer.EnsureAppointmentsGeneratedAsync(secondStartUtc, secondEndUtc, TestContext.Current.CancellationToken);

        var appointments = await db.Appointments
            .Where(appointment => appointment.RecurringRule != null && appointment.RecurringRule.Id == rule.Id)
            .OrderBy(appointment => appointment.StartDate)
            .ToListAsync(TestContext.Current.CancellationToken);

        appointments.Count.ShouldBe(7);
        appointments.Select(appointment => appointment.StartDate).Distinct().Count().ShouldBe(7);
    }

    [Fact]
    public async Task EnsureAppointmentsGeneratedAsync_RepeatedRun_PreservesCompletedOccurrence()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();

        var rule = await TestDataFactory.CreateDailyRuleAsync(
            db,
            new DateTime(2025, 11, 14, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 11, 20, 23, 59, 59, DateTimeKind.Utc),
            "Elena",
            "Volkova",
            "Piano",
            TestContext.Current.CancellationToken);
        var startUtc = new DateTime(2025, 11, 14, 0, 0, 0, DateTimeKind.Utc);
        var endUtc = new DateTime(2025, 11, 20, 23, 59, 59, DateTimeKind.Utc);

        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);
        var completed = await db.Appointments
            .FirstAsync(
                appointment => appointment.RecurringRule != null && appointment.RecurringRule.Id == rule.Id,
                TestContext.Current.CancellationToken);
        completed.Status = AppointmentStatus.Completed;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await materializer.EnsureAppointmentsGeneratedAsync(startUtc, endUtc, TestContext.Current.CancellationToken);

        var persistedStatus = await db.Appointments
            .Where(appointment => appointment.Id == completed.Id)
            .Select(appointment => appointment.Status)
            .SingleAsync(TestContext.Current.CancellationToken);
        persistedStatus.ShouldBe(AppointmentStatus.Completed);
    }

    [Fact]
    public async Task EnsureAppointmentsGeneratedAsync_ProviderHasMultiWeekVacation_SkipsOccurrencesInsideVacation()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();
        var provider = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var rule = await TestDataFactory.CreateDailyRuleAsync(
            db,
            new DateTime(2025, 11, 14, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 10, 23, 59, 59, DateTimeKind.Utc),
            "Anna",
            "Morozova",
            "Voice",
            TestContext.Current.CancellationToken);
        rule.Provider = provider;
        await db.UserVacations.AddAsync(new UserVacation
        {
            Id = Ulid.NewUlid(),
            UserId = provider.Id,
            User = provider,
            StartDate = new DateTime(2025, 11, 15, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2025, 12, 1, 0, 0, 0, DateTimeKind.Utc)
        }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        await materializer.EnsureAppointmentsGeneratedAsync(
            new DateTime(2025, 11, 14, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 3, 23, 59, 59, DateTimeKind.Utc),
            TestContext.Current.CancellationToken);

        var starts = await db.Appointments
            .Where(appointment => appointment.RecurringRule != null && appointment.RecurringRule.Id == rule.Id)
            .OrderBy(appointment => appointment.StartDate)
            .Select(appointment => appointment.StartDate)
            .ToListAsync(TestContext.Current.CancellationToken);
        starts.ShouldBe([
            new DateTime(2025, 11, 14, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 1, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 2, 15, 0, 0, DateTimeKind.Utc),
            new DateTime(2025, 12, 3, 15, 0, 0, DateTimeKind.Utc)
        ]);
    }
}
