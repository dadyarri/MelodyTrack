using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Testcontainers.PostgreSql;

namespace MelodyTrack.Backend.Tests;

public class RecurringAppointmentMaterializerTests(RecurringAppointmentMaterializerFixture fixture)
    : IClassFixture<RecurringAppointmentMaterializerFixture>
{
    [Fact]
    public async Task EnsureAppointmentsGeneratedAsync_RepeatedForSameWeek_DoesNotCreateDuplicates()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();

        var rule = await CreateWeeklyRuleAsync(db, TestContext.Current.CancellationToken);
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
        await using var scope = fixture.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var materializer = scope.ServiceProvider.GetRequiredService<IRecurringAppointmentMaterializer>();

        var rule = await CreateDailyRuleAsync(db, TestContext.Current.CancellationToken);
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

    private static async Task<AppointmentRecurrenceRule> CreateWeeklyRuleAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Weekly, cancellationToken);
        var client = new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = "Ivan",
            LastName = "Petrov",
            Contacts = new ClientContacts { Id = Ulid.NewUlid() }
        };
        var service = new Service
        {
            Id = Ulid.NewUlid(),
            Name = "Vocal"
        };
        var rule = new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = new DateTime(2025, 11, 10, 12, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc),
            RecurrenceType = recurrenceType,
            RecurrencePattern = 1 + 4
        };

        await db.RecurrenceRules.AddAsync(rule, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }

    private static async Task<AppointmentRecurrenceRule> CreateDailyRuleAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Daily, cancellationToken);
        var client = new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = "Maria",
            LastName = "Sokolova",
            Contacts = new ClientContacts { Id = Ulid.NewUlid() }
        };
        var service = new Service
        {
            Id = Ulid.NewUlid(),
            Name = "Guitar"
        };
        var rule = new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = new DateTime(2025, 11, 14, 15, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2025, 11, 20, 23, 59, 59, DateTimeKind.Utc),
            RecurrenceType = recurrenceType,
            RecurrencePattern = 1
        };

        await db.RecurrenceRules.AddAsync(rule, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return rule;
    }
}

public class RecurringAppointmentMaterializerFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _dbContainer;

    public ServiceProvider Services { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        _dbContainer = new PostgreSqlBuilder("postgres:latest")
            .WithDatabase("testdb")
            .WithPortBinding(5432, true)
            .Build();

        await _dbContainer.StartAsync();

        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseNpgsql(_dbContainer.GetConnectionString()));
        services.AddScoped<IAppointmentDeletionService, AppointmentDeletionService>();
        services.AddScoped<IRecurringAppointmentService, RecurringAppointmentService>();
        services.AddScoped<IRecurringAppointmentMaterializer, RecurringAppointmentMaterializer>();
        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();

        if (_dbContainer is not null)
        {
            await _dbContainer.StopAsync();
            await _dbContainer.DisposeAsync();
        }
    }
}
