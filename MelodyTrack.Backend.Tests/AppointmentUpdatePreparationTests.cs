using MelodyTrack.Backend.Api.Schedule;
using MelodyTrack.Backend.Api.Schedule.Requests;
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
public sealed class AppointmentUpdatePreparationTests(MelodyTrackFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PrepareAsync_ExistingSeriesPatternChange_PreservesRuleAndHistory()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var start = new DateTime(2030, 6, 3, 12, 0, 0, DateTimeKind.Utc);
        var rule = await TestDataFactory.CreateWeeklyRuleAsync(db, start, start.AddDays(21), 1, "Test", "Client", "Lesson", ct);
        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(), Client = rule.Client, Service = rule.Service, Provider = rule.Provider,
            RecurringRule = rule, RecurrenceOrdinal = 0, StartDate = start, EndDate = start.AddHours(1), Status = AppointmentStatus.Completed, IsDeleted = false
        };
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync(ct);
        var service = scope.ServiceProvider.GetRequiredService<AppointmentUpdatePreparationService>();

        var result = await service.PrepareAsync(appointment, new UpdateAppointmentRequest
        {
            RecurrenceTypeId = rule.RecurrenceType.Id, RecurrencePattern = 2, StartDate = start
        }, ct);

        result.Error.ShouldBe(AppointmentUpdatePreparationError.RecurringPatternChange);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        (await db.RecurrenceRules.SingleAsync(item => item.Id == rule.Id, ct)).RecurrencePattern.ShouldBe(1);
        var history = await db.Appointments.SingleAsync(item => item.Id == appointment.Id, ct);
        history.StartDate.ShouldBe(start);
        history.Status.ShouldBe(AppointmentStatus.Completed);
        history.RecurrenceOrdinal.ShouldBe(0);
    }
}
