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

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<Ulid>()
            .HaveConversion<UlidToBytesConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Role>().HasData(
            [
                new Role
                {
                    Id = Ulid.Parse("01K7PVV27FAPWXRHE8H93T0DZM"),
                    RoleName = UserRoles.Superuser,
                    DisplayName = "Суперпользователь"
                },
                new Role
                {
                    Id = Ulid.Parse("01K7PVV92WS673S9YRXHYWTHEN"),
                    RoleName = UserRoles.Admin,
                    DisplayName = "Администратор"
                },
                new Role
                {
                    Id = Ulid.Parse("01K7PVVCR9D4HJ5DH1HEYTQQG9"),
                    RoleName = UserRoles.User,
                    DisplayName = "Пользователь"
                }
            ]
        );
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<InviteCode> InviteCodes { get; set; }
    public DbSet<RecoveryCode> RecoveryCodes { get; set; }
    public DbSet<Session> Sessions { get; set; }
    public DbSet<PasswordRestorationRequest> PasswordRestorationRequests { get; set; }
    public DbSet<Client> Clients { get; set; }
}