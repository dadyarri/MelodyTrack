using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services.RecurringTasks;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using MelodyTrack.Backend.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class RecurringTaskServiceTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task AppointmentReminder_ReschedulingToSameDay_RegeneratesTaskWithNewDeduplicationKey()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recurringTaskService = scope.ServiceProvider.GetRequiredService<IRecurringTaskService>();

        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Иван", "Петров", TestContext.Current.CancellationToken);
        client.Contacts.Phone = "+79991234567";

        var service = await TestDataFactory.CreateServiceAsync(db, "Математика", TestContext.Current.CancellationToken);
        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = DateTime.UtcNow.AddHours(12),
            EndDate = DateTime.UtcNow.AddHours(13),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };

        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var initialTasks = await recurringTaskService.GetTasksAsync(
            "Europe/Moscow",
            RecurringTaskType.AppointmentReminder,
            RecurringTaskListStatus.Open,
            TestContext.Current.CancellationToken);
        var initialTask = initialTasks.ShouldHaveSingleItem();

        var completeResult = await recurringTaskService.CompleteAsync(new MelodyTrack.Backend.Api.Tasks.Requests.CompleteRecurringTaskRequest
        {
            Timezone = "Europe/Moscow",
            RuleId = initialTask.RuleId,
            Type = initialTask.Type,
            DeduplicationKey = initialTask.DeduplicationKey,
            ClientId = initialTask.ClientId,
            AppointmentId = initialTask.AppointmentId,
            PreparedMessage = initialTask.PreparedMessage
        }, admin, TestContext.Current.CancellationToken);

        completeResult.Succeeded.ShouldBeTrue();

        appointment.StartDate = appointment.StartDate.AddHours(1);
        appointment.EndDate = appointment.EndDate.AddHours(1);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rescheduledTasks = await recurringTaskService.GetTasksAsync(
            "Europe/Moscow",
            RecurringTaskType.AppointmentReminder,
            RecurringTaskListStatus.Open,
            TestContext.Current.CancellationToken);
        var rescheduledTask = rescheduledTasks.ShouldHaveSingleItem();

        rescheduledTask.DeduplicationKey.ShouldNotBe(initialTask.DeduplicationKey);
        rescheduledTask.AppointmentId.ShouldBe(appointment.Id);
    }

    [Fact]
    public async Task DelayAsync_HidesTaskUntilRequestedTime_AndShowsItAgainAfterDelayExpires()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recurringTaskService = scope.ServiceProvider.GetRequiredService<IRecurringTaskService>();

        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Мария", "Иванова", TestContext.Current.CancellationToken);
        client.Contacts.Phone = "+79991234568";

        var service = await TestDataFactory.CreateServiceAsync(db, "Английский", TestContext.Current.CancellationToken);
        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = DateTime.UtcNow.AddHours(8),
            EndDate = DateTime.UtcNow.AddHours(9),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };

        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var openTasks = await recurringTaskService.GetTasksAsync(
            "Europe/Moscow",
            RecurringTaskType.AppointmentReminder,
            RecurringTaskListStatus.Open,
            TestContext.Current.CancellationToken);
        var openTask = openTasks.ShouldHaveSingleItem();

        var delayedUntilUtc = DateTime.UtcNow.AddHours(2);
        var delayResult = await recurringTaskService.DelayAsync(new MelodyTrack.Backend.Api.Tasks.Requests.DelayRecurringTaskRequest
        {
            Timezone = "Europe/Moscow",
            RuleId = openTask.RuleId,
            Type = openTask.Type,
            DeduplicationKey = openTask.DeduplicationKey,
            ClientId = openTask.ClientId,
            AppointmentId = openTask.AppointmentId,
            DelayUntilUtc = delayedUntilUtc
        }, admin, TestContext.Current.CancellationToken);

        delayResult.Succeeded.ShouldBeTrue();
        delayResult.Status.ShouldBe(RecurringTaskStatus.Delayed);

        var hiddenTasks = await recurringTaskService.GetTasksAsync(
            "Europe/Moscow",
            RecurringTaskType.AppointmentReminder,
            RecurringTaskListStatus.Open,
            TestContext.Current.CancellationToken);
        hiddenTasks.ShouldBeEmpty();

        var delayedTasks = await recurringTaskService.GetTasksAsync(
            "Europe/Moscow",
            RecurringTaskType.AppointmentReminder,
            RecurringTaskListStatus.Delayed,
            TestContext.Current.CancellationToken);
        delayedTasks.ShouldHaveSingleItem().DelayedUntilUtc.ShouldNotBeNull();

        var execution = await db.RecurringTaskExecutions.SingleAsync(
            item => item.DeduplicationKey == openTask.DeduplicationKey,
            TestContext.Current.CancellationToken);
        execution.DelayedUntilUtc = DateTime.UtcNow.AddMinutes(-5);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var reopenedTasks = await recurringTaskService.GetTasksAsync(
            "Europe/Moscow",
            RecurringTaskType.AppointmentReminder,
            RecurringTaskListStatus.Open,
            TestContext.Current.CancellationToken);
        reopenedTasks.ShouldHaveSingleItem().DeduplicationKey.ShouldBe(openTask.DeduplicationKey);
    }

    [Fact]
    public async Task DebtorReminder_ForOlderDebt_KeepsOnlyCurrentWeeklyReminder()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recurringTaskService = scope.ServiceProvider.GetRequiredService<IRecurringTaskService>();

        var client = await TestDataFactory.CreateClientAsync(db, "Елена", "Сидорова", TestContext.Current.CancellationToken);
        client.Contacts.Phone = "+79991234569";

        var service = await TestDataFactory.CreateServiceAsync(db, "Фортепиано", TestContext.Current.CancellationToken);
        await db.ServicePriceHistory.AddAsync(new ServicePrice
        {
            Id = Ulid.NewUlid(),
            Service = service,
            Price = 1_000m,
            EffectiveDate = DateTime.UtcNow.AddMonths(-2)
        }, TestContext.Current.CancellationToken);

        var debtStartedAtUtc = DateTime.UtcNow.AddDays(-16);
        await db.Appointments.AddAsync(new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = debtStartedAtUtc,
            EndDate = debtStartedAtUtc.AddHours(1),
            Status = AppointmentStatus.Completed,
            IsDeleted = false
        }, TestContext.Current.CancellationToken);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tasks = await recurringTaskService.GetTasksAsync(
            "Europe/Moscow",
            RecurringTaskType.DebtorReminder,
            RecurringTaskListStatus.Open,
            TestContext.Current.CancellationToken);

        tasks.Count.ShouldBe(1);
        tasks.ShouldContain(task => task.DeduplicationKey.Contains("debtor-reminder", StringComparison.Ordinal));
        tasks.Count(task => task.Type == "debtor-reminder").ShouldBe(1);

        var debtStartDate = DateOnly.FromDateTime(DateTimeUtils.ConvertDateToTimezone(debtStartedAtUtc, "Europe/Moscow"));
        tasks.ShouldContain(task => task.BusinessDate == debtStartDate.AddDays(14));
    }

    [Fact]
    public async Task DebtorReminder_OnFirstDayAfterDebt_GeneratesOnlyFirstReminder()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recurringTaskService = scope.ServiceProvider.GetRequiredService<IRecurringTaskService>();

        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Кузнецова", TestContext.Current.CancellationToken);
        client.Contacts.Phone = "+79991234570";

        var service = await TestDataFactory.CreateServiceAsync(db, "Сольфеджио", TestContext.Current.CancellationToken);
        await db.ServicePriceHistory.AddAsync(new ServicePrice
        {
            Id = Ulid.NewUlid(),
            Service = service,
            Price = 1_000m,
            EffectiveDate = DateTime.UtcNow.AddMonths(-2)
        }, TestContext.Current.CancellationToken);

        var debtStartedAtUtc = DateTime.UtcNow.AddDays(-1);
        await db.Appointments.AddAsync(new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = debtStartedAtUtc,
            EndDate = debtStartedAtUtc.AddHours(1),
            Status = AppointmentStatus.Completed,
            IsDeleted = false
        }, TestContext.Current.CancellationToken);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tasks = await recurringTaskService.GetTasksAsync(
            "Europe/Moscow",
            RecurringTaskType.DebtorReminder,
            RecurringTaskListStatus.Open,
            TestContext.Current.CancellationToken);

        tasks.Count.ShouldBe(1);
        tasks.ShouldContain(task => task.BusinessDate == DateOnly.FromDateTime(DateTimeUtils.ConvertDateToTimezone(debtStartedAtUtc, "Europe/Moscow")).AddDays(1));
    }

    [Fact]
    public async Task DebtorReminder_BetweenThresholds_KeepsOnlyCurrentStageReminder()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recurringTaskService = scope.ServiceProvider.GetRequiredService<IRecurringTaskService>();

        var client = await TestDataFactory.CreateClientAsync(db, "Ольга", "Миронова", TestContext.Current.CancellationToken);
        client.Contacts.Phone = "+79991234571";

        var service = await TestDataFactory.CreateServiceAsync(db, "Вокал", TestContext.Current.CancellationToken);
        await db.ServicePriceHistory.AddAsync(new ServicePrice
        {
            Id = Ulid.NewUlid(),
            Service = service,
            Price = 1_000m,
            EffectiveDate = DateTime.UtcNow.AddMonths(-2)
        }, TestContext.Current.CancellationToken);

        var debtStartedAtUtc = DateTime.UtcNow.AddDays(-5);
        await db.Appointments.AddAsync(new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = debtStartedAtUtc,
            EndDate = debtStartedAtUtc.AddHours(1),
            Status = AppointmentStatus.Completed,
            IsDeleted = false
        }, TestContext.Current.CancellationToken);

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tasks = await recurringTaskService.GetTasksAsync(
            "Europe/Moscow",
            RecurringTaskType.DebtorReminder,
            RecurringTaskListStatus.Open,
            TestContext.Current.CancellationToken);

        var debtStartDate = DateOnly.FromDateTime(DateTimeUtils.ConvertDateToTimezone(debtStartedAtUtc, "Europe/Moscow"));
        tasks.Count.ShouldBe(1);
        tasks.ShouldContain(task => task.BusinessDate == debtStartDate.AddDays(3));
    }

    [Fact]
    public async Task CustomTask_ForExistingClient_AppearsInOpenTasks()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var recurringTaskService = scope.ServiceProvider.GetRequiredService<IRecurringTaskService>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Ирина", "Орлова", TestContext.Current.CancellationToken);
        client.Contacts.Phone = "+79991234572";

        await db.CustomTasks.AddAsync(new CustomTask
        {
            Id = Ulid.NewUlid(),
            Client = client,
            ClientId = client.Id,
            RecipientName = "Орлова Ирина",
            Title = "Уточнить расписание",
            MessageText = "Здравствуйте! Уточняю удобное время.",
            DueAtUtc = DateTime.UtcNow.AddHours(1),
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = admin.Id
        }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tasks = await recurringTaskService.GetTasksAsync(
            "Europe/Moscow",
            RecurringTaskType.CustomTask,
            RecurringTaskListStatus.Open,
            TestContext.Current.CancellationToken);

        tasks.ShouldHaveSingleItem().RelatedPersonDisplayName.ShouldContain("Орлова");
    }
}
