using System.Net;
using System.Net.Http.Headers;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.Schedule.Endpoints;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class RecurringAppointmentUpdateTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task UpdateAppointment_MovingRecurringOccurrence_DetachesItAndPreventsRematerializingOriginalSlot()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal lesson", TestContext.Current.CancellationToken);
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Daily, TestContext.Current.CancellationToken);

        var rule = new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = user,
            StartDate = new DateTime(2026, 05, 12, 14, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 05, 17, 23, 59, 59, DateTimeKind.Utc),
            RecurrenceType = recurrenceType,
            RecurrencePattern = 1
        };

        await db.RecurrenceRules.AddAsync(rule, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rangeStart = new DateTime(2026, 05, 12, 0, 0, 0, DateTimeKind.Utc);
        var rangeEnd = new DateTime(2026, 05, 17, 23, 59, 59, DateTimeKind.Utc);
        await materializer.EnsureAppointmentsGeneratedAsync(rangeStart, rangeEnd, TestContext.Current.CancellationToken);

        var occurrence = await db.Appointments
            .Where(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == new DateTime(2026, 05, 14, 14, 0, 0, DateTimeKind.Utc))
            .FirstAsync(TestContext.Current.CancellationToken);

        var movedStartDate = new DateTime(2026, 05, 14, 17, 0, 0, DateTimeKind.Utc);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, _) = await App.Client.PATCHAsync<UpdateAppointmentEndpoint, UpdateAppointmentRequest, NoContent>(new UpdateAppointmentRequest
        {
            Id = occurrence.Id,
            StartDate = movedStartDate
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();
        await materializer.EnsureAppointmentsGeneratedAsync(rangeStart, rangeEnd, TestContext.Current.CancellationToken);

        var deletedOriginalOccurrences = await db.Appointments
            .CountAsync(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == occurrence.StartDate &&
                item.IsDeleted,
                TestContext.Current.CancellationToken);

        var activeOriginalOccurrences = await db.Appointments
            .CountAsync(item =>
                item.RecurringRule != null &&
                item.RecurringRule.Id == rule.Id &&
                item.StartDate == occurrence.StartDate &&
                !item.IsDeleted,
                TestContext.Current.CancellationToken);

        var movedOccurrences = await db.Appointments
            .Where(item => item.StartDate == movedStartDate && !item.IsDeleted)
            .Select(item => new
            {
                item.Id,
                HasRecurringRule = item.RecurringRule != null
            })
            .ToListAsync(TestContext.Current.CancellationToken);

        deletedOriginalOccurrences.ShouldBe(1);
        activeOriginalOccurrences.ShouldBe(0);
        movedOccurrences.Count.ShouldBe(1);
        movedOccurrences[0].HasRecurringRule.ShouldBeFalse();
    }
}
