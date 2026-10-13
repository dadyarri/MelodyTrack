using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public sealed class SchedulingMigrationTests(MelodyTrackFixture fixture) : IntegrationTestBase(fixture)
{
    private const string PreviousMigration = "20260923170016_ReplaceHomeworkBbcodeWithStructuredDocuments";

    [Fact]
    public async Task MigrateAsync_LegacyVacationsAndAppointments_PreservesIdentitiesAndRecordsOnlyProvenCancellations()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, ct);
        var start = new DateTime(2030, 6, 3, 12, 0, 0, DateTimeKind.Utc);
        var rule = await TestDataFactory.CreateDailyRuleAsync(db, start, start.AddDays(30), "Test", "Client", "Lesson", ct);
        rule.Provider = teacher;
        var vacation = new Vacation { Id = Ulid.NewUlid(), ClientId = rule.Client.Id, StartDate = start.AddHours(-1), EndDate = start.AddDays(20) };
        var staffVacation = new Vacation { Id = Ulid.NewUlid(), UserId = teacher.Id, StartDate = start.AddDays(25), EndDate = start.AddDays(28) };
        var planned = Instance(rule, start, AppointmentStatus.Planned);
        var manualCancellation = Instance(rule, start.AddDays(1), AppointmentStatus.Cancelled);
        var completed = Instance(rule, start.AddDays(2), AppointmentStatus.Completed);
        db.Vacations.AddRange(vacation, staffVacation);
        db.Appointments.AddRange(planned, manualCancellation, completed);
        await db.SaveChangesAsync(ct);
        var migrator = db.GetService<IMigrator>();

        try
        {
            await migrator.MigrateAsync(PreviousMigration, ct);
            await migrator.MigrateAsync(cancellationToken: ct);
            db.ChangeTracker.Clear();

            var migratedVacation = await db.Vacations.SingleAsync(item => item.Id == vacation.Id, ct);
            migratedVacation.ClientId.ShouldBe(rule.Client.Id);
            migratedVacation.StartDate.ShouldBe(vacation.StartDate);
            migratedVacation.EndDate.ShouldBe(vacation.EndDate);
            migratedVacation.Version.ShouldBe(1);
            (await db.Vacations.SingleAsync(item => item.Id == staffVacation.Id, ct)).UserId.ShouldBe(teacher.Id);
            (await db.Appointments.SingleAsync(item => item.Id == planned.Id, ct)).Status.ShouldBe(AppointmentStatus.Cancelled);
            (await db.Appointments.SingleAsync(item => item.Id == completed.Id, ct)).Status.ShouldBe(AppointmentStatus.Completed);
            (await db.Appointments.SingleAsync(item => item.Id == manualCancellation.Id, ct)).Status.ShouldBe(AppointmentStatus.Cancelled);
            var interruptions = await db.SchedulingInterruptions.Where(item => item.VacationId == vacation.Id).ToListAsync(ct);
            interruptions.ShouldContain(item => item.AppointmentId == planned.Id && item.RecurrenceOrdinal == 0);
            interruptions.ShouldNotContain(item => item.AppointmentId == manualCancellation.Id || item.AppointmentId == completed.Id);
            interruptions.ShouldContain(item => item.AppointmentId == null && item.RecurrenceOrdinal == 19);
            interruptions.ShouldNotContain(item => item.AppointmentId == null && item.RecurrenceOrdinal <= 2);
            (await db.RecurrenceRules.SingleAsync(item => item.Id == rule.Id, ct)).Timezone.ShouldBe("UTC");
        }
        finally
        {
            await migrator.MigrateAsync(cancellationToken: ct);
        }
    }

    private static Appointment Instance(AppointmentRecurrenceRule rule, DateTime start, AppointmentStatus status) => new()
    {
        Id = Ulid.NewUlid(), Client = rule.Client, Service = rule.Service, Provider = rule.Provider,
        RecurringRule = rule, RecurrenceOrdinal = (long)(start - rule.StartDate).TotalDays,
        StartDate = start, EndDate = start.AddHours(1), Status = status, IsDeleted = false
    };
}
