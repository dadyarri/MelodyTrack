using MelodyTrack.Backend.Data;
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
}
