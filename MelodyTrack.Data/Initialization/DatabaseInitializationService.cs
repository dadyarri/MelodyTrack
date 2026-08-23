using System.Security.Cryptography;
using System.Text;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Core.Configuration;
using MelodyTrack.Core.Security;
using MelodyTrack.Data.Configuration;
using MelodyTrack.Data.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Data.Initialization;

public sealed class DatabaseInitializationService(
    AppDbContext db,
    IPersonalDataBackfillService personalDataBackfill,
    IPersonalDataProtector personalDataProtector,
    IOptions<InitializationOptions> initializationOptions,
    IOptions<PublicUrlOptions> publicUrlOptions,
    CredentialHasher credentialHasher,
    DevelopmentDemoDataSeeder developmentDemoDataSeeder,
    DevelopmentFullDemoDataSeeder developmentFullDemoDataSeeder,
    TimeProvider timeProvider,
    ILogger<DatabaseInitializationService> logger)
{
    private const int DevelopmentSeedVersion = 7;
    private const string DevelopmentEmail = "dev.superuser@melodytrack.local";
    private const string DevelopmentPassword = "MelodyTrack-Development-Only!";
    private const string DevelopmentTotpSecret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    public async Task RunAsync(InitializationMode mode, CancellationToken cancellationToken)
    {
        logger.LogInformation("Initializing MelodyTrack database in {Mode} mode", mode);

        await db.Database.MigrateAsync(cancellationToken);
        await personalDataBackfill.BackfillAsync(cancellationToken);
        await InitializeQuartzAsync(cancellationToken);
        await DatabaseInvariantValidator.ValidateAsync(db, cancellationToken);

        switch (mode)
        {
            case InitializationMode.Production:
                await EnsureProductionBootstrapInviteAsync(cancellationToken);
                break;
            case InitializationMode.Development:
                await ApplyDevelopmentSeedUpgradesAsync(cancellationToken);
                break;
            case InitializationMode.Test:
                await EnsureTestBaselineAsync(cancellationToken);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, null);
        }

        logger.LogInformation("MelodyTrack database initialization completed in {Mode} mode", mode);
    }

    private async Task InitializeQuartzAsync(CancellationToken cancellationToken)
    {
        var configuredPath = initializationOptions.Value.QuartzSqlPath;
        var path = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(AppContext.BaseDirectory, configuredPath);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Quartz database initialization script was not found.", path);
        }

        var sql = await File.ReadAllTextAsync(path, cancellationToken);
        await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
    }

    private async Task EnsureProductionBootstrapInviteAsync(CancellationToken cancellationToken)
    {
        var superuserRole = await db.Roles.SingleAsync(role => role.RoleName == UserRoles.Superuser, cancellationToken);
        var hasSuperuser = await db.Users
            .AsNoTracking()
            .AnyAsync(user => user.Role.RoleName == UserRoles.Superuser, cancellationToken);
        if (hasSuperuser)
        {
            return;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var invite = await db.InviteCodes
            .Include(item => item.Role)
            .FirstOrDefaultAsync(
                item => item.Role.RoleName == UserRoles.Superuser && !item.WasUsed && item.ValidUntil >= nowUtc,
                cancellationToken);

        if (invite is null)
        {
            invite = new InviteCode
            {
                Id = Ulid.NewUlid(),
                Code = Ulid.NewUlid(),
                Role = superuserRole,
                ValidUntil = nowUtc.AddDays(2)
            };
            await db.InviteCodes.AddAsync(invite, cancellationToken);
        }

        var inviteReference = DescribeSecret("invite", invite.Code.ToString());
        await db.AuditLogs.AddAsync(new AuditLog
        {
            Id = Ulid.NewUlid(),
            CreatedAtUtc = nowUtc,
            Category = "security",
            Action = "superuser_bootstrap_invite_available",
            EntityType = "invite",
            EntityId = invite.Id.ToString(),
            Details = $"Приглашение: {inviteReference}; действует до: {invite.ValidUntil:O}"
        }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);

        if (initializationOptions.Value.LogBootstrapSecrets)
        {
            var url = $"{publicUrlOptions.Value.BaseUrl.TrimEnd('/')}/invite/{invite.Code}";
            logger.LogWarning(
                "Superuser was not created yet. Bootstrap invite {InviteReference} can be used at {InviteUrl}",
                inviteReference,
                url);
            return;
        }

        logger.LogWarning(
            "Superuser was not created yet. Bootstrap invite {InviteReference} exists until {ValidUntilUtc}. Full link logging is disabled; enable Initialization:LogBootstrapSecrets only for controlled recovery.",
            inviteReference,
            invite.ValidUntil);
    }

    private async Task ApplyDevelopmentSeedUpgradesAsync(CancellationToken cancellationToken)
    {
        for (var version = 1; version <= DevelopmentSeedVersion; version++)
        {
            var action = $"development_seed_v{version}";
            if (await db.AuditLogs.AsNoTracking().AnyAsync(log => log.Action == action, cancellationToken))
            {
                continue;
            }

            switch (version)
            {
                case 1:
                    await ApplyDevelopmentSeedVersionOneAsync(cancellationToken);
                    break;
                case 2:
                    await ApplyDevelopmentSeedVersionTwoAsync(cancellationToken);
                    break;
                case 3:
                    await developmentDemoDataSeeder.SeedAsync(cancellationToken);
                    break;
                case 4:
                    await developmentDemoDataSeeder.EnsureProviderAssignmentsAsync(cancellationToken);
                    break;
                case 5:
                    await developmentDemoDataSeeder.SeedUpcomingAppointmentsAsync(cancellationToken);
                    break;
                case 6:
                    await developmentFullDemoDataSeeder.SeedAsync(cancellationToken);
                    break;
                case 7:
                    await ApplyDevelopmentSeedVersionSevenAsync(cancellationToken);
                    break;
                default:
                    throw new InvalidOperationException($"Development seed upgrade {version} is not implemented.");
            }

            await db.AuditLogs.AddAsync(CreateInitializationMarker(action), cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task ApplyDevelopmentSeedVersionOneAsync(CancellationToken cancellationToken)
    {
        var superuserRole = await db.Roles.SingleAsync(role => role.RoleName == UserRoles.Superuser, cancellationToken);
        var emailBlindIndex = personalDataProtector.HashEmailBlindIndex(DevelopmentEmail);
        var provider = await db.Users.SingleOrDefaultAsync(
            user => user.EmailBlindIndex == emailBlindIndex,
            cancellationToken);
        if (provider is null)
        {
            provider = new User
            {
                Id = Ulid.Parse("01K00000000000000000000001"),
                FirstName = "Development",
                LastName = "Superuser",
                Email = DevelopmentEmail,
                Password = credentialHasher.HashPassword(DevelopmentPassword),
                Role = superuserRole
            };
            await db.Users.AddAsync(provider, cancellationToken);
        }

        var source = await db.ClientSources.SingleOrDefaultAsync(item => item.Name == "Демо", cancellationToken);
        if (source is null)
        {
            source = new ClientSource { Id = Ulid.Parse("01K00000000000000000000002"), Name = "Демо" };
            await db.ClientSources.AddAsync(source, cancellationToken);
        }

        var clientId = Ulid.Parse("01K00000000000000000000003");
        var client = await db.Clients.SingleOrDefaultAsync(item => item.Id == clientId, cancellationToken);
        if (client is null)
        {
            client = new Client
            {
                Id = clientId,
                FirstName = "Анна",
                LastName = "Демонстрационная",
                CreatedAtUtc = new DateTime(2026, 1, 10, 9, 0, 0, DateTimeKind.Utc),
                Source = source,
                Contacts = new ClientContacts
                {
                    Id = Ulid.Parse("01K00000000000000000000004"),
                    Email = "demo.client@melodytrack.local"
                }
            };
            await db.Clients.AddAsync(client, cancellationToken);
        }

        var serviceId = Ulid.Parse("01K00000000000000000000005");
        var service = await db.Services.SingleOrDefaultAsync(item => item.Id == serviceId, cancellationToken);
        if (service is null)
        {
            service = new Service
            {
                Id = serviceId,
                Name = "Демо-занятие",
                PublicName = "Индивидуальное занятие",
                Description = "Демонстрационная услуга"
            };
            await db.Services.AddAsync(service, cancellationToken);
            await db.ServicePriceHistory.AddAsync(new ServicePrice
            {
                Id = Ulid.Parse("01K00000000000000000000006"),
                Service = service,
                Price = 2_000m,
                EffectiveDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }, cancellationToken);
        }

        var appointmentId = Ulid.Parse("01K00000000000000000000007");
        if (!await db.Appointments.AnyAsync(item => item.Id == appointmentId, cancellationToken))
        {
            await db.Appointments.AddAsync(new Appointment
            {
                Id = appointmentId,
                Client = client,
                Service = service,
                Provider = provider,
                StartDate = new DateTime(2026, 2, 2, 12, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 2, 2, 13, 0, 0, DateTimeKind.Utc),
                Status = AppointmentStatus.Completed,
                IsDeleted = false
            }, cancellationToken);
            await db.Payments.AddAsync(new Payment
            {
                Id = Ulid.Parse("01K00000000000000000000008"),
                Client = client,
                Service = service,
                Amount = 2_000m,
                Date = new DateTime(2026, 2, 2, 13, 0, 0, DateTimeKind.Utc),
                Description = "Оплата демонстрационного занятия"
            }, cancellationToken);
        }

        var categoryId = Ulid.Parse("01K00000000000000000000009");
        var category = await db.ExpenseCategories.SingleOrDefaultAsync(item => item.Id == categoryId, cancellationToken);
        if (category is null)
        {
            category = new ExpenseCategory { Id = categoryId, Name = "Демо-расходы" };
            await db.ExpenseCategories.AddAsync(category, cancellationToken);
        }

        var expenseId = Ulid.Parse("01K0000000000000000000000A");
        if (!await db.Expenses.AnyAsync(item => item.Id == expenseId, cancellationToken))
        {
            await db.Expenses.AddAsync(new Expense
            {
                Id = expenseId,
                Category = category,
                Amount = 500m,
                Date = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc),
                Description = "Демонстрационный расход"
            }, cancellationToken);
        }

        logger.LogInformation(
            "Development identity is {DevelopmentEmail}; its deterministic non-production password is documented in MelodyTrack.Init/README.md",
            DevelopmentEmail);
    }

    private async Task ApplyDevelopmentSeedVersionTwoAsync(CancellationToken cancellationToken)
    {
        var emailBlindIndex = personalDataProtector.HashEmailBlindIndex(DevelopmentEmail);
        var provider = await db.Users.SingleAsync(
            user => user.EmailBlindIndex == emailBlindIndex,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(provider.TotpSecret))
        {
            provider.TotpSecret = DevelopmentTotpSecret;
            logger.LogInformation(
                "Development identity {DevelopmentEmail} now has a configured second factor; its development-only TOTP setup key is documented in MelodyTrack.Init/README.md",
                DevelopmentEmail);
            return;
        }

        logger.LogInformation(
            "Development identity {DevelopmentEmail} already has a configured second factor; preserving it",
            DevelopmentEmail);
    }

    private async Task ApplyDevelopmentSeedVersionSevenAsync(CancellationToken cancellationToken)
    {
        var emailBlindIndex = personalDataProtector.HashEmailBlindIndex(DevelopmentEmail);
        var provider = await db.Users.SingleAsync(
            user => user.EmailBlindIndex == emailBlindIndex,
            cancellationToken);
        provider.Password = credentialHasher.HashPassword(DevelopmentPassword);
        logger.LogInformation("Development identity {DevelopmentEmail} password was upgraded to the current credential format", DevelopmentEmail);
    }

    private async Task EnsureTestBaselineAsync(CancellationToken cancellationToken)
    {
        const string action = "test_seed_v1";
        if (await db.AuditLogs.AsNoTracking().AnyAsync(log => log.Action == action, cancellationToken))
        {
            return;
        }

        var superuserRole = await db.Roles.SingleAsync(role => role.RoleName == UserRoles.Superuser, cancellationToken);
        var inviteId = Ulid.Parse("01K0000000000000000000000B");
        if (!await db.InviteCodes.AnyAsync(invite => invite.Id == inviteId, cancellationToken))
        {
            await db.InviteCodes.AddAsync(new InviteCode
            {
                Id = inviteId,
                Code = Ulid.Parse("01K0000000000000000000000C"),
                Role = superuserRole,
                ValidUntil = new DateTime(2099, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }, cancellationToken);
        }

        await db.AuditLogs.AddAsync(CreateInitializationMarker(action), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private AuditLog CreateInitializationMarker(string action)
    {
        return new AuditLog
        {
            Id = action switch
            {
                "development_seed_v1" => Ulid.Parse("01K0000000000000000000000D"),
                "development_seed_v2" => Ulid.Parse("01K0000000000000000000000F"),
                "development_seed_v3" => Ulid.Parse("01K0000000000000000000000G"),
                "development_seed_v4" => Ulid.Parse("01K0000000000000000000000H"),
                "development_seed_v5" => Ulid.Parse("01K0000000000000000000000J"),
                "development_seed_v6" => Ulid.Parse("01K0000000000000000000000K"),
                "test_seed_v1" => Ulid.Parse("01K0000000000000000000000E"),
                _ => Ulid.NewUlid()
            },
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Category = "initialization",
            Action = action,
            EntityType = "database",
            Details = "Applied by MelodyTrack.Init"
        };
    }

    private static string DescribeSecret(string prefix, string secret)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
        return $"{prefix}#{Convert.ToHexString(digest)[..12]}";
    }
}
