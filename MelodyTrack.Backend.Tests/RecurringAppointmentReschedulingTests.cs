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
public sealed class RecurringAppointmentReschedulingTests(MelodyTrackFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task RescheduleAsync_All_PreservesHistoryCancellationsAndDeletedOccurrences()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, ct);
        var rule = await TestDataFactory.CreateWeeklyRuleAsync(db, Utc(5, 27), Utc(6, 24), 1, "Test", "Client", "Lesson", ct);
        rule.Provider = teacher;
        rule.Timezone = "Europe/Moscow";
        var history = Instance(rule, Utc(5, 27), 0, AppointmentStatus.Completed);
        var planned = Instance(rule, Utc(6, 3), 7, AppointmentStatus.Planned);
        var cancelled = Instance(rule, Utc(6, 10), 14, AppointmentStatus.Cancelled);
        var deleted = Instance(rule, Utc(6, 17), 21, AppointmentStatus.Planned);
        deleted.IsDeleted = true;
        var burned = Instance(rule, Utc(6, 24), 28, AppointmentStatus.Burned);
        db.Appointments.AddRange(history, planned, cancelled, deleted, burned);
        await db.SaveChangesAsync(ct);
        var service = new RecurringAppointmentReschedulingService(db,
            scope.ServiceProvider.GetRequiredService<IRecurringAppointmentService>(),
            scope.ServiceProvider.GetRequiredService<ISchedulingAvailabilityService>(), new FixedClock(Utc(6, 1)));

        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            (await service.RescheduleAsync(planned, Utc(6, 3).AddHours(1), true, ct)).ShouldBeNull();
            await transaction.CommitAsync(ct);
        }
        await scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>()
            .EnsureAppointmentsGeneratedAsync(Utc(5, 27), Utc(6, 25), ct);
        db.ChangeTracker.Clear();

        var appointments = await db.Appointments.OrderBy(item => item.StartDate).ToListAsync(ct);
        appointments.Count.ShouldBe(5);
        var preservedHistory = appointments.Single(item => item.Id == history.Id);
        preservedHistory.StartDate.ShouldBe(Utc(5, 27));
        preservedHistory.Status.ShouldBe(AppointmentStatus.Completed);
        preservedHistory.RecurringRuleId.ShouldBe(rule.Id);
        appointments.Single(item => item.Id == planned.Id).StartDate.ShouldBe(Utc(6, 3).AddHours(1));
        var preservedCancellation = appointments.Single(item => item.Id == cancelled.Id);
        preservedCancellation.StartDate.ShouldBe(Utc(6, 10).AddHours(1));
        preservedCancellation.Status.ShouldBe(AppointmentStatus.Cancelled);
        appointments.Single(item => item.Id == deleted.Id).IsDeleted.ShouldBeTrue();
        appointments.Single(item => item.Id == burned.Id).StartDate.ShouldBe(Utc(6, 24));
        appointments.Single(item => item.Id == burned.Id).Status.ShouldBe(AppointmentStatus.Burned);
        appointments.ShouldNotContain(item => item.StartDate == Utc(6, 24).AddHours(1));
        (await db.RecurrenceRules.CountAsync(ct)).ShouldBe(2);
    }

    private static DateTime Utc(int month, int day) => new(2030, month, day, 12, 0, 0, DateTimeKind.Utc);
    private static Appointment Instance(AppointmentRecurrenceRule rule, DateTime start, long ordinal, AppointmentStatus status) => new()
    {
        Id = Ulid.NewUlid(), Client = rule.Client, Service = rule.Service, Provider = rule.Provider,
        RecurringRule = rule, RecurrenceOrdinal = ordinal, StartDate = start, EndDate = start.AddHours(1), Status = status, IsDeleted = false
    };
    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
