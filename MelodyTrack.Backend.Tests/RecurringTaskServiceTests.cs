using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services.RecurringTasks;
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

        var initialTasks = await recurringTaskService.GetDueTasksAsync("Europe/Moscow", RecurringTaskType.AppointmentReminder, TestContext.Current.CancellationToken);
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

        var rescheduledTasks = await recurringTaskService.GetDueTasksAsync("Europe/Moscow", RecurringTaskType.AppointmentReminder, TestContext.Current.CancellationToken);
        var rescheduledTask = rescheduledTasks.ShouldHaveSingleItem();

        rescheduledTask.DeduplicationKey.ShouldNotBe(initialTask.DeduplicationKey);
        rescheduledTask.AppointmentId.ShouldBe(appointment.Id);
    }
}
