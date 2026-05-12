using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Tests.Infrastructure;

public static class TestDataFactory
{
    public static async Task<User> CreateAuthorizedScheduleUserAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var userRole = await db.Roles.FirstAsync(role => role.RoleName == UserRoles.User, cancellationToken);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            FirstName = "Schedule",
            LastName = "Operator",
            Email = $"{Ulid.NewUlid()}@example.com",
            Password = "hash",
            Role = userRole
        };

        await db.Users.AddAsync(user, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return user;
    }

    public static async Task<Client> CreateClientAsync(
        AppDbContext db,
        string firstName,
        string lastName,
        CancellationToken cancellationToken)
    {
        var client = new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = firstName,
            LastName = lastName,
            Contacts = new ClientContacts
            {
                Id = Ulid.NewUlid()
            }
        };

        await db.Clients.AddAsync(client, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return client;
    }

    public static async Task<Service> CreateServiceAsync(AppDbContext db, string name, CancellationToken cancellationToken)
    {
        var service = new Service
        {
            Id = Ulid.NewUlid(),
            Name = name
        };

        await db.Services.AddAsync(service, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return service;
    }

    public static async Task<AppointmentRecurrenceRule> CreateDailyRuleAsync(
        AppDbContext db,
        DateTime startDate,
        DateTime endDate,
        string clientFirstName,
        string clientLastName,
        string serviceName,
        CancellationToken cancellationToken)
    {
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Daily, cancellationToken);
        var client = await CreateClientAsync(db, clientFirstName, clientLastName, cancellationToken);
        var service = await CreateServiceAsync(db, serviceName, cancellationToken);

        var rule = new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = startDate,
            EndDate = endDate,
            RecurrenceType = recurrenceType,
            RecurrencePattern = 1
        };

        await db.RecurrenceRules.AddAsync(rule, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    public static async Task<AppointmentRecurrenceRule> CreateWeeklyRuleAsync(
        AppDbContext db,
        DateTime startDate,
        DateTime endDate,
        int recurrencePattern,
        string clientFirstName,
        string clientLastName,
        string serviceName,
        CancellationToken cancellationToken)
    {
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Weekly, cancellationToken);
        var client = await CreateClientAsync(db, clientFirstName, clientLastName, cancellationToken);
        var service = await CreateServiceAsync(db, serviceName, cancellationToken);

        var rule = new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = startDate,
            EndDate = endDate,
            RecurrenceType = recurrenceType,
            RecurrencePattern = recurrencePattern
        };

        await db.RecurrenceRules.AddAsync(rule, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }
}
