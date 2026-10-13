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
public sealed class VacationLifecycleTests(MelodyTrackFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EndAsync_OwnActiveVacation_RetainsHistoryAndRequiresExplicitRestoration(bool clientOwner)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = new FixedClock(Utc(3, 10));
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Test", "Client", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Lesson", TestContext.Current.CancellationToken);
        var actor = teacher;
        if (clientOwner)
        {
            actor = new User { Id = Ulid.NewUlid(), FirstName = "Portal", LastName = "User", Email = $"{Ulid.NewUlid()}@example.com", Password = "hash",
                ClientId = client.Id, Client = client, Role = await db.Roles.FirstAsync(item => item.RoleName == UserRoles.Client, TestContext.Current.CancellationToken) };
            db.Users.Add(actor);
        }
        var appointment = new Appointment { Id = Ulid.NewUlid(), Client = client, Provider = teacher, Service = service,
            StartDate = Utc(4, 12), EndDate = Utc(4, 13), Status = AppointmentStatus.Planned, IsDeleted = false };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        var recurrence = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentService>();
        var interruptions = new SchedulingInterruptionService(db, recurrence, audit, clock);
        var vacations = new VacationService(db, interruptions, audit, clock);
        var subjectType = clientOwner ? VacationRequestSubjectType.Client : VacationRequestSubjectType.Staff;
        Vacation vacation;
        await using (var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            var created = await vacations.ApplyAsync(subjectType, clientOwner ? client.Id : teacher.Id,
                VacationRequestAction.Create, null, null, Utc(3, 9), Utc(7, 18), true, TestContext.Current.CancellationToken);
            created.Failure.ShouldBe(VacationRequestWorkflowFailure.None);
            vacation = created.Vacation!;
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
        clock.Now = Utc(3, 11);

        var ended = await vacations.EndAsync(vacation.Id, vacation.Version, actor, TestContext.Current.CancellationToken);
        var restoration = new VacationRestorationService(db, recurrence, scope.ServiceProvider.GetRequiredService<ISchedulingAvailabilityService>(), interruptions, clock);
        var candidates = await restoration.GetCandidatesAsync(vacation.Id, TestContext.Current.CancellationToken);

        ended.Failure.ShouldBe(VacationRequestWorkflowFailure.None);
        vacation.StartDate.ShouldBe(Utc(3, 9));
        vacation.EndDate.ShouldBe(clock.Now);
        vacation.OriginalEndDate.ShouldBe(Utc(7, 18));
        appointment.Status.ShouldBe(AppointmentStatus.Cancelled);
        candidates.ShouldHaveSingleItem().Available.ShouldBeTrue();

        var restored = await restoration.RestoreAsync(vacation.Id, vacation.Version, [candidates[0].Id], actor, TestContext.Current.CancellationToken);

        restored.Failure.ShouldBe(VacationRequestWorkflowFailure.None);
        appointment.Status.ShouldBe(AppointmentStatus.Planned);
        (await restoration.GetCandidatesAsync(vacation.Id, TestContext.Current.CancellationToken)).ShouldBeEmpty();
    }

    [Fact]
    public async Task EndAsync_StaleVersion_PreservesVacation()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actor = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var vacation = new Vacation { Id = Ulid.NewUlid(), UserId = actor.Id, StartDate = Utc(3, 9), EndDate = Utc(7, 18), Version = 2 };
        db.Vacations.Add(vacation);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        var clock = new FixedClock(Utc(3, 11));
        var interruptions = new SchedulingInterruptionService(db, scope.ServiceProvider.GetRequiredService<IRecurringAppointmentService>(), audit, clock);
        var service = new VacationService(db, interruptions, audit, clock);

        var result = await service.EndAsync(vacation.Id, 1, actor, TestContext.Current.CancellationToken);

        result.Failure.ShouldBe(VacationRequestWorkflowFailure.Conflict);
        vacation.EndDate.ShouldBe(Utc(7, 18));
        vacation.EndedAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task EndAsync_AnotherTeacherVacation_RejectsOperation()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var owner = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var actor = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var vacation = new Vacation { Id = Ulid.NewUlid(), UserId = owner.Id, StartDate = Utc(3, 9), EndDate = Utc(7, 18) };
        db.Vacations.Add(vacation);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        var clock = new FixedClock(Utc(3, 11));
        var interruptions = new SchedulingInterruptionService(db, scope.ServiceProvider.GetRequiredService<IRecurringAppointmentService>(), audit, clock);

        var result = await new VacationService(db, interruptions, audit, clock).EndAsync(vacation.Id, 1, actor, TestContext.Current.CancellationToken);

        result.Failure.ShouldBe(VacationRequestWorkflowFailure.Forbidden);
        vacation.EndedAtUtc.ShouldBeNull();
    }

    [Fact]
    public async Task ApplyAsync_ShortenedVacationWithUnmaterializedSeries_PreventsAutomaticRestorationOutsideReadHorizon()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Test", "Client", TestContext.Current.CancellationToken);
        var lesson = await TestDataFactory.CreateServiceAsync(db, "Lesson", TestContext.Current.CancellationToken);
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(item => item.Type == AppointmentRecurrenceType.Weekly, TestContext.Current.CancellationToken);
        var rule = new AppointmentRecurrenceRule { Id = Ulid.NewUlid(), Client = client, Provider = teacher, Service = lesson,
            StartDate = Utc(5, 12), EndDate = Utc(30, 18), RecurrenceType = recurrenceType, RecurrencePattern = 4 };
        db.RecurrenceRules.Add(rule);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var clock = new FixedClock(Utc(1, 10));
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();
        var recurrence = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentService>();
        var interruptions = new SchedulingInterruptionService(db, recurrence, audit, clock);
        var vacations = new VacationService(db, interruptions, audit, clock);
        Vacation vacation;
        await using (var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            var created = await vacations.ApplyAsync(VacationRequestSubjectType.Staff, teacher.Id, VacationRequestAction.Create,
                null, null, Utc(2, 9), Utc(30, 18), false, TestContext.Current.CancellationToken);
            created.Failure.ShouldBe(VacationRequestWorkflowFailure.None);
            vacation = created.Vacation!;
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }
        await using (var transaction = await db.Database.BeginTransactionAsync(TestContext.Current.CancellationToken))
        {
            var updated = await vacations.ApplyAsync(VacationRequestSubjectType.Staff, teacher.Id, VacationRequestAction.Update,
                vacation.Id, vacation.Version, Utc(2, 9), Utc(4, 18), false, TestContext.Current.CancellationToken);
            updated.Failure.ShouldBe(VacationRequestWorkflowFailure.None);
            await transaction.CommitAsync(TestContext.Current.CancellationToken);
        }

        await scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>().EnsureAppointmentsGeneratedAsync(Utc(1, 0), Utc(30, 23), TestContext.Current.CancellationToken);
        var restoration = new VacationRestorationService(db, recurrence, scope.ServiceProvider.GetRequiredService<ISchedulingAvailabilityService>(), interruptions, clock);
        var candidates = await restoration.GetCandidatesAsync(vacation.Id, TestContext.Current.CancellationToken);

        (await db.Appointments.CountAsync(item => item.RecurringRuleId == rule.Id, TestContext.Current.CancellationToken)).ShouldBe(0);
        candidates.Count.ShouldBe(4);
        candidates.ShouldAllBe(item => item.Available);

        var restored = await restoration.RestoreAsync(vacation.Id, vacation.Version, [candidates[0].Id], teacher, TestContext.Current.CancellationToken);
        await scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>().EnsureAppointmentsGeneratedAsync(Utc(1, 0), Utc(30, 23), TestContext.Current.CancellationToken);

        restored.Failure.ShouldBe(VacationRequestWorkflowFailure.None);
        (await db.Appointments.CountAsync(item => item.RecurringRuleId == rule.Id, TestContext.Current.CancellationToken)).ShouldBe(1);
        (await restoration.GetCandidatesAsync(vacation.Id, TestContext.Current.CancellationToken)).Count.ShouldBe(3);
    }

    private static DateTime Utc(int day, int hour) => new(2030, 6, day, hour, 0, 0, DateTimeKind.Utc);
    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public DateTime Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => new(Now);
    }
}
