using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Data.ValueConverters;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<InviteCode> InviteCodes { get; set; }
    public DbSet<RecoveryCode> RecoveryCodes { get; set; }
    public DbSet<Session> Sessions { get; set; }
    public DbSet<PasswordRestorationRequest> PasswordRestorationRequests { get; set; }
    public DbSet<Client> Clients { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<Service> Services { get; set; }
    public DbSet<ServicePrice> ServicePriceHistory { get; set; }
    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<AppointmentRecurrenceRule> RecurrenceRules { get; set; }
    public DbSet<RecurrenceType> RecurrenceTypes { get; set; }
    public DbSet<Expense> Expenses { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<RequestReplay> RequestReplays { get; set; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<Ulid>()
            .HaveConversion<UlidToBytesConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("fuzzystrmatch");

        modelBuilder.Entity<Role>().HasData(new Role
        {
            Id = Ulid.Parse("01K7PVV27FAPWXRHE8H93T0DZM"),
            RoleName = UserRoles.Superuser,
            DisplayName = "Суперпользователь"
        }, new Role
        {
            Id = Ulid.Parse("01K7PVV92WS673S9YRXHYWTHEN"),
            RoleName = UserRoles.Admin,
            DisplayName = "Администратор"
        }, new Role
        {
            Id = Ulid.Parse("01K7PVVCR9D4HJ5DH1HEYTQQG9"),
            RoleName = UserRoles.User,
            DisplayName = "Пользователь"
        });

        modelBuilder.Entity<RecurrenceType>().HasData(new RecurrenceType
        {
            Id = Ulid.Parse("01K9BSF5RW3GGMQM1HTQG0QF7D"),
            DisplayName = "Ежедневно",
            Type = AppointmentRecurrenceType.Daily
        }, new RecurrenceType
        {
            Id = Ulid.Parse("01K9BSFGRN8RHRDHNXHJX7JT93"),
            DisplayName = "Еженедельно",
            Type = AppointmentRecurrenceType.Weekly
        }, new RecurrenceType
        {
            Id = Ulid.Parse("01K9BSFNNFHYZK6ME71N5Y1M01"),
            DisplayName = "Ежемесячно",
            Type = AppointmentRecurrenceType.Monthly
        });

        modelBuilder.Entity<RequestReplay>()
            .HasIndex(e => new { e.Endpoint, e.ReplayKey })
            .IsUnique();
    }
}
