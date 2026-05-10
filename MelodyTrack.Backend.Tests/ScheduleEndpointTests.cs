using System.Net;
using System.Net.Http.Headers;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Schedule.Endpoints;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Api.Schedule.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class ScheduleEndpointTests(MelodyTrackFixture app) : TestBase<MelodyTrackFixture>
{
    [Fact]
    public async Task CreateRecurringAppointment_AssignsNonEmptyRecurringRuleId()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await CreateAuthorizedUserAsync(db, TestContext.Current.CancellationToken);
        var client = await CreateClientAsync(db, TestContext.Current.CancellationToken);
        var service = await CreateServiceAsync(db, TestContext.Current.CancellationToken);
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Weekly, TestContext.Current.CancellationToken);

        app.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var request = new CreateAppointmentRequest
        {
            ClientId = client.Id,
            ServiceId = service.Id,
            StartDate = new DateTime(2026, 05, 11, 12, 0, 0, DateTimeKind.Utc),
            RecurrenceTypeId = recurrenceType.Id,
            PatternEndDate = new DateTime(2026, 05, 31, 23, 59, 59, DateTimeKind.Utc),
            RecurrencePattern = 1 + 4
        };

        var (rsp, res) = await app.Client.POSTAsync<CreateAppointmentEndpoint, CreateAppointmentRequest, CreateEntityResponse>(request);

        rsp.StatusCode.ShouldBe(HttpStatusCode.Created);
        res.ShouldNotBeNull();

        db.ChangeTracker.Clear();

        var appointment = await db.Appointments
            .Include(item => item.RecurringRule)
            .FirstAsync(item => item.Id == res.Id, TestContext.Current.CancellationToken);

        appointment.RecurringRule.ShouldNotBeNull();
        appointment.RecurringRule!.Id.ShouldNotBe(Ulid.Empty);
    }

    [Fact]
    public async Task UpdateAppointment_AddRecurringRule_AssignsNonEmptyRecurringRuleId()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await CreateAuthorizedUserAsync(db, TestContext.Current.CancellationToken);
        var client = await CreateClientAsync(db, TestContext.Current.CancellationToken);
        var service = await CreateServiceAsync(db, TestContext.Current.CancellationToken);
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Daily, TestContext.Current.CancellationToken);

        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = new DateTime(2026, 05, 12, 14, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 05, 12, 15, 0, 0, DateTimeKind.Utc),
            IsCompleted = false,
            IsCanceled = false,
            IsDeleted = false
        };

        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        app.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, _) = await app.Client.PATCHAsync<UpdateAppointmentEndpoint, UpdateAppointmentRequest, NoContent>(new UpdateAppointmentRequest
        {
            Id = appointment.Id,
            StartDate = appointment.StartDate,
            RecurrenceTypeId = recurrenceType.Id,
            RecurrencePattern = 1
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();

        var updatedAppointment = await db.Appointments
            .Include(item => item.RecurringRule)
            .FirstAsync(item => item.Id == appointment.Id, TestContext.Current.CancellationToken);

        updatedAppointment.RecurringRule.ShouldNotBeNull();
        updatedAppointment.RecurringRule!.Id.ShouldNotBe(Ulid.Empty);
    }

    [Fact]
    public async Task GetAppointments_ReturnsRecurringRuleForRecurringAppointment()
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await CreateAuthorizedUserAsync(db, TestContext.Current.CancellationToken);
        var client = await CreateClientAsync(db, TestContext.Current.CancellationToken);
        var service = await CreateServiceAsync(db, TestContext.Current.CancellationToken);
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Weekly, TestContext.Current.CancellationToken);
        var recurrenceRuleId = Ulid.NewUlid();

        var recurrenceRule = new AppointmentRecurrenceRule
        {
            Id = recurrenceRuleId,
            Client = client,
            Service = service,
            StartDate = new DateTime(2026, 05, 13, 16, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 05, 31, 23, 59, 59, DateTimeKind.Utc),
            RecurrenceType = recurrenceType,
            RecurrencePattern = 1 + 4
        };

        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = recurrenceRule.StartDate,
            EndDate = recurrenceRule.StartDate.AddHours(1),
            IsCompleted = false,
            IsCanceled = false,
            IsDeleted = false,
            RecurringRule = recurrenceRule
        };

        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        app.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var (rsp, res) = await app.Client.GETAsync<GetAppointmentsEndpoint, GetAppointmentsRequest, GetAppointmentsResponse>(new GetAppointmentsRequest
        {
            Timezone = "UTC",
            StartDate = new DateTime(2026, 05, 12, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 05, 14, 23, 59, 59, DateTimeKind.Utc)
        });

        rsp.StatusCode.ShouldBe(HttpStatusCode.OK);
        res.ShouldNotBeNull();
        res.Appointments.Count.ShouldBe(1);
        res.Appointments[0].RecurringRule.ShouldNotBeNull();
        res.Appointments[0].RecurringRule!.Id.ShouldBe(recurrenceRuleId);
        res.Appointments[0].RecurringRule!.Key.ShouldBe("weekly");
    }

    private static async Task<User> CreateAuthorizedUserAsync(AppDbContext db, CancellationToken ct)
    {
        var role = await db.Roles.FirstAsync(item => item.RoleName == UserRoles.User, ct);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            Email = $"schedule-{Ulid.NewUlid()}@example.com",
            FirstName = "Schedule",
            LastName = "Tester",
            Password = "hash",
            Role = role
        };

        await db.Users.AddAsync(user, ct);
        await db.SaveChangesAsync(ct);

        return user;
    }

    private static async Task<Client> CreateClientAsync(AppDbContext db, CancellationToken ct)
    {
        var client = new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = "Nina",
            LastName = "Petrova",
            Contacts = new ClientContacts
            {
                Id = Ulid.NewUlid()
            }
        };

        await db.Clients.AddAsync(client, ct);
        await db.SaveChangesAsync(ct);

        return client;
    }

    private static async Task<Service> CreateServiceAsync(AppDbContext db, CancellationToken ct)
    {
        var service = new Service
        {
            Id = Ulid.NewUlid(),
            Name = $"Service-{Ulid.NewUlid()}"
        };

        await db.Services.AddAsync(service, ct);
        await db.SaveChangesAsync(ct);

        return service;
    }
}
