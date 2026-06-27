using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Data.ValueConverters;
using MelodyTrack.Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Data;

public class AppDbContext : DbContext
{
    private readonly IPersonalDataProtector? _personalDataProtector;

    public AppDbContext(DbContextOptions<AppDbContext> options, IPersonalDataProtector? personalDataProtector = null) : base(options)
    {
        _personalDataProtector = personalDataProtector;
    }

    public DbSet<User> Users { get; set; }
    public DbSet<Role> Roles { get; set; }
    public DbSet<InviteCode> InviteCodes { get; set; }
    public DbSet<RecoveryCode> RecoveryCodes { get; set; }
    public DbSet<Session> Sessions { get; set; }
    public DbSet<PasswordRestorationRequest> PasswordRestorationRequests { get; set; }
    public DbSet<ClientPortalLoginLink> ClientPortalLoginLinks { get; set; }
    public DbSet<Client> Clients { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<Service> Services { get; set; }
    public DbSet<ServicePrice> ServicePriceHistory { get; set; }
    public DbSet<Appointment> Appointments { get; set; }
    public DbSet<AppointmentRecurrenceRule> RecurrenceRules { get; set; }
    public DbSet<RecurrenceType> RecurrenceTypes { get; set; }
    public DbSet<Expense> Expenses { get; set; }
    public DbSet<ExpenseCategory> ExpenseCategories { get; set; }
    public DbSet<ClientSource> ClientSources { get; set; }
    public DbSet<UserWorkingHoursDay> UserWorkingHoursDays { get; set; }
    public DbSet<UserVacation> UserVacations { get; set; }
    public DbSet<UserOnboardingState> UserOnboardingStates { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<RequestReplay> RequestReplays { get; set; }
    public DbSet<RecurringTaskRule> RecurringTaskRules { get; set; }
    public DbSet<RecurringTaskExecution> RecurringTaskExecutions { get; set; }
    public DbSet<CustomTask> CustomTasks { get; set; }
    public DbSet<Course> Courses { get; set; }
    public DbSet<CourseLevel> CourseLevels { get; set; }
    public DbSet<CourseBlock> CourseBlocks { get; set; }
    public DbSet<CourseBranch> CourseBranches { get; set; }
    public DbSet<CourseTheme> CourseThemes { get; set; }
    public DbSet<CourseThemeDependency> CourseThemeDependencies { get; set; }
    public DbSet<CourseEnrollment> CourseEnrollments { get; set; }
    public DbSet<CourseEnrollmentTheme> CourseEnrollmentThemes { get; set; }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder
            .Properties<Ulid>()
            .HaveConversion<UlidToBytesConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        if (_personalDataProtector is not null)
        {
            modelBuilder.Entity<User>().Property(e => e.Email).HasConversion(new EncryptedRequiredStringConverter(_personalDataProtector));
            modelBuilder.Entity<User>().Property(e => e.Phone).HasConversion(new EncryptedStringConverter(_personalDataProtector));
            modelBuilder.Entity<User>().Property(e => e.Telegram).HasConversion(new EncryptedStringConverter(_personalDataProtector));
            modelBuilder.Entity<User>().Property(e => e.Vk).HasConversion(new EncryptedStringConverter(_personalDataProtector));

            modelBuilder.Entity<ClientContacts>().Property(e => e.Phone).HasConversion(new EncryptedStringConverter(_personalDataProtector));
            modelBuilder.Entity<ClientContacts>().Property(e => e.Email).HasConversion(new EncryptedStringConverter(_personalDataProtector));
            modelBuilder.Entity<ClientContacts>().Property(e => e.Telegram).HasConversion(new EncryptedStringConverter(_personalDataProtector));
            modelBuilder.Entity<ClientContacts>().Property(e => e.Vk).HasConversion(new EncryptedStringConverter(_personalDataProtector));

            modelBuilder.Entity<ClientPortalLoginLink>().Property(e => e.PinCode).HasConversion(new EncryptedStringConverter(_personalDataProtector));
        }

        modelBuilder.Entity<User>()
            .HasIndex(e => e.EmailBlindIndex)
            .IsUnique();

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
        }, new Role
        {
            Id = Ulid.Parse("01JZQTKRQJQ2WQ3EY3P99RYJ79"),
            RoleName = UserRoles.Client,
            DisplayName = "Клиент"
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

        var recurringTaskSeededAtUtc = new DateTime(2026, 6, 4, 0, 0, 0, DateTimeKind.Utc);

        modelBuilder.Entity<RecurringTaskRule>().HasData(
            new RecurringTaskRule
            {
                Id = Ulid.Parse("01JWN36Z4HZ3D3GCQQ10SFNNM0"),
                Name = "Напоминание о записи",
                Type = RecurringTaskType.AppointmentReminder,
                IsEnabled = true,
                MessageTemplate = "Здравствуйте, {Client.FirstName}! Напоминаем, что {When} в {Appointment.StartTime} у вас запланировано занятие.",
                OffsetMinutes = 24 * 60,
                CooldownDays = null,
                CreatedAtUtc = recurringTaskSeededAtUtc,
                UpdatedAtUtc = recurringTaskSeededAtUtc
            },
            new RecurringTaskRule
            {
                Id = Ulid.Parse("01JWN37C3NCG2TP5S89KX2M48Q"),
                Name = "Поздравление с днём рождения",
                Type = RecurringTaskType.BirthdayGreeting,
                IsEnabled = true,
                MessageTemplate = "Здравствуйте, {Client.FirstName}! Поздравляем вас с днём рождения! Желаем хорошего дня, отличного настроения и вдохновения.",
                OffsetMinutes = null,
                CooldownDays = 365,
                CreatedAtUtc = recurringTaskSeededAtUtc,
                UpdatedAtUtc = recurringTaskSeededAtUtc
            },
            new RecurringTaskRule
            {
                Id = Ulid.Parse("01JWN37N45DQ5CNVZ8BPKSWHR0"),
                Name = "Связаться после пробного занятия",
                Type = RecurringTaskType.TrialFollowUp,
                IsEnabled = true,
                MessageTemplate = "Здравствуйте, {Client.FirstName}! Спасибо, что пришли на пробное занятие. Хотите подобрать удобное время для следующих занятий?",
                OffsetMinutes = 24 * 60,
                CooldownDays = null,
                CreatedAtUtc = recurringTaskSeededAtUtc,
                UpdatedAtUtc = recurringTaskSeededAtUtc
            },
            new RecurringTaskRule
            {
                Id = Ulid.Parse("01JWN381RT0F9ZJQTFQNYJH80A"),
                Name = "Напомнить о занятиях",
                Type = RecurringTaskType.InactiveClientReminder,
                IsEnabled = true,
                MessageTemplate = "Здравствуйте, {Client.FirstName}! Вы давно не были на занятиях. Хотите подобрать удобное время для следующего занятия?",
                OffsetMinutes = null,
                CooldownDays = 7,
                CreatedAtUtc = recurringTaskSeededAtUtc,
                UpdatedAtUtc = recurringTaskSeededAtUtc
            },
            new RecurringTaskRule
            {
                Id = Ulid.Parse("01JWN38EN9FA1BT39WHX1XPJMS"),
                Name = "Отправить расписание преподавателю",
                Type = RecurringTaskType.TeacherDailySchedule,
                IsEnabled = true,
                MessageTemplate = "Здравствуйте, {Teacher.FirstName}! Отправляем ваше расписание на {Date}.",
                OffsetMinutes = null,
                CooldownDays = 1,
                CreatedAtUtc = recurringTaskSeededAtUtc,
                UpdatedAtUtc = recurringTaskSeededAtUtc
            },
            new RecurringTaskRule
            {
                Id = Ulid.Parse("01JX22E8S0Y8Q5NQMY4H3G2H5A"),
                Name = "Напомнить о долге через день",
                Type = RecurringTaskType.DebtorReminder,
                IsEnabled = true,
                MessageTemplate = "Здравствуйте, {Client.FirstName}! Напоминаем, что у вас есть задолженность. Напишите нам, пожалуйста, если хотите уточнить сумму или подобрать удобный способ оплаты.",
                OffsetMinutes = 24 * 60,
                CooldownDays = null,
                CreatedAtUtc = recurringTaskSeededAtUtc,
                UpdatedAtUtc = recurringTaskSeededAtUtc
            },
            new RecurringTaskRule
            {
                Id = Ulid.Parse("01JX22E8S0Y8Q5NQMY4H3G2H5B"),
                Name = "Напомнить о долге через 3 дня",
                Type = RecurringTaskType.DebtorReminder,
                IsEnabled = true,
                MessageTemplate = "Здравствуйте, {Client.FirstName}! Напоминаем о задолженности по занятиям. Если удобно, можем помочь с оплатой или ответить на вопросы.",
                OffsetMinutes = 3 * 24 * 60,
                CooldownDays = null,
                CreatedAtUtc = recurringTaskSeededAtUtc,
                UpdatedAtUtc = recurringTaskSeededAtUtc
            },
            new RecurringTaskRule
            {
                Id = Ulid.Parse("01JX22E8S0Y8Q5NQMY4H3G2H5C"),
                Name = "Напомнить о долге через неделю",
                Type = RecurringTaskType.DebtorReminder,
                IsEnabled = true,
                MessageTemplate = "Здравствуйте, {Client.FirstName}! У вас по-прежнему есть задолженность. Напишите нам, если нужна помощь с оплатой или хотите обсудить детали.",
                OffsetMinutes = 7 * 24 * 60,
                CooldownDays = null,
                CreatedAtUtc = recurringTaskSeededAtUtc,
                UpdatedAtUtc = recurringTaskSeededAtUtc
            },
            new RecurringTaskRule
            {
                Id = Ulid.Parse("01JX22E8S0Y8Q5NQMY4H3G2H5D"),
                Name = "Напоминать о долге каждую неделю",
                Type = RecurringTaskType.DebtorReminder,
                IsEnabled = true,
                MessageTemplate = "Здравствуйте, {Client.FirstName}! Напоминаем, что задолженность всё ещё не закрыта. Если нужна помощь или удобный вариант оплаты, мы на связи.",
                OffsetMinutes = 7 * 24 * 60,
                CooldownDays = 7,
                CreatedAtUtc = recurringTaskSeededAtUtc,
                UpdatedAtUtc = recurringTaskSeededAtUtc
            });

        modelBuilder.Entity<RequestReplay>()
            .HasIndex(e => new { e.Endpoint, e.ReplayKey })
            .IsUnique();

        modelBuilder.Entity<User>()
            .HasIndex(e => e.ClientId)
            .IsUnique()
            .HasFilter("\"ClientId\" IS NOT NULL");

        modelBuilder.Entity<User>()
            .HasOne(e => e.Client)
            .WithMany()
            .HasForeignKey(e => e.ClientId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ClientPortalLoginLink>()
            .HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RecurringTaskExecution>()
            .HasIndex(e => e.DeduplicationKey)
            .IsUnique();

        modelBuilder.Entity<RecurringTaskExecution>()
            .HasIndex(e => new { e.RuleId, e.Status });

        modelBuilder.Entity<RecurringTaskExecution>()
            .HasIndex(e => new { e.ClientId, e.RuleId, e.CompletedAtUtc });

        modelBuilder.Entity<RecurringTaskExecution>()
            .HasIndex(e => e.AppointmentId);

        modelBuilder.Entity<RecurringTaskExecution>()
            .HasOne(e => e.Rule)
            .WithMany()
            .HasForeignKey(e => e.RuleId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<RecurringTaskExecution>()
            .HasOne(e => e.Client)
            .WithMany()
            .HasForeignKey(e => e.ClientId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RecurringTaskExecution>()
            .HasOne(e => e.Teacher)
            .WithMany()
            .HasForeignKey(e => e.TeacherId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RecurringTaskExecution>()
            .HasOne(e => e.Appointment)
            .WithMany()
            .HasForeignKey(e => e.AppointmentId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RecurringTaskExecution>()
            .HasOne(e => e.CompletedByUser)
            .WithMany()
            .HasForeignKey(e => e.CompletedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RecurringTaskExecution>()
            .HasOne(e => e.CancelledByUser)
            .WithMany()
            .HasForeignKey(e => e.CancelledByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<RecurringTaskExecution>()
            .HasOne(e => e.DelayedByUser)
            .WithMany()
            .HasForeignKey(e => e.DelayedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CustomTask>()
            .HasIndex(e => new { e.ClientId, e.DueAtUtc });

        modelBuilder.Entity<CustomTask>()
            .HasIndex(e => e.DelayedUntilUtc);

        modelBuilder.Entity<CustomTask>()
            .HasOne(e => e.Client)
            .WithMany()
            .HasForeignKey(e => e.ClientId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CustomTask>()
            .HasOne(e => e.CreatedByUser)
            .WithMany()
            .HasForeignKey(e => e.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CustomTask>()
            .HasOne(e => e.CompletedByUser)
            .WithMany()
            .HasForeignKey(e => e.CompletedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CustomTask>()
            .HasOne(e => e.CancelledByUser)
            .WithMany()
            .HasForeignKey(e => e.CancelledByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CustomTask>()
            .HasOne(e => e.DelayedByUser)
            .WithMany()
            .HasForeignKey(e => e.DelayedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Course>()
            .HasIndex(e => e.Name);

        modelBuilder.Entity<CourseLevel>()
            .HasIndex(e => new { e.CourseId, e.Order })
            .IsUnique();

        modelBuilder.Entity<CourseLevel>()
            .HasOne(e => e.Course)
            .WithMany(e => e.Levels)
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CourseBlock>()
            .HasIndex(e => new { e.CourseId, e.Order })
            .IsUnique();

        modelBuilder.Entity<CourseBlock>()
            .HasOne(e => e.Course)
            .WithMany(e => e.Blocks)
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CourseBranch>()
            .HasIndex(e => new { e.BlockId, e.Order })
            .IsUnique();

        modelBuilder.Entity<CourseBranch>()
            .HasOne(e => e.Block)
            .WithMany(e => e.Branches)
            .HasForeignKey(e => e.BlockId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CourseTheme>()
            .HasIndex(e => new { e.BranchId, e.Order })
            .IsUnique();

        modelBuilder.Entity<CourseTheme>()
            .HasOne(e => e.Branch)
            .WithMany(e => e.Themes)
            .HasForeignKey(e => e.BranchId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CourseThemeDependency>()
            .HasIndex(e => new { e.ThemeId, e.DependsOnThemeId })
            .IsUnique();

        modelBuilder.Entity<CourseThemeDependency>()
            .HasOne(e => e.Theme)
            .WithMany(e => e.Dependencies)
            .HasForeignKey(e => e.ThemeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CourseThemeDependency>()
            .HasOne(e => e.DependsOnTheme)
            .WithMany(e => e.RequiredForThemes)
            .HasForeignKey(e => e.DependsOnThemeId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CourseEnrollment>()
            .HasIndex(e => new { e.ClientId, e.CourseId })
            .IsUnique();

        modelBuilder.Entity<CourseEnrollment>()
            .HasOne(e => e.Client)
            .WithMany()
            .HasForeignKey(e => e.ClientId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CourseEnrollment>()
            .HasOne(e => e.Course)
            .WithMany()
            .HasForeignKey(e => e.CourseId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CourseEnrollmentTheme>()
            .HasIndex(e => new { e.EnrollmentId, e.CourseThemeId })
            .IsUnique();

        modelBuilder.Entity<CourseEnrollmentTheme>()
            .HasOne(e => e.Enrollment)
            .WithMany(e => e.Themes)
            .HasForeignKey(e => e.EnrollmentId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CourseEnrollmentTheme>()
            .HasOne(e => e.CourseTheme)
            .WithMany()
            .HasForeignKey(e => e.CourseThemeId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Appointment>()
            .HasOne(e => e.CourseTheme)
            .WithMany()
            .HasForeignKey(e => e.CourseThemeId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Expense>()
            .HasOne(e => e.Category)
            .WithMany()
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Client>()
            .HasOne(e => e.Source)
            .WithMany()
            .HasForeignKey(e => e.SourceId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<UserWorkingHoursDay>()
            .HasOne(e => e.User)
            .WithMany(e => e.WorkingHours)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserWorkingHoursDay>()
            .HasIndex(e => new { e.UserId, e.DayOfWeek })
            .IsUnique();

        modelBuilder.Entity<UserVacation>()
            .HasOne(e => e.User)
            .WithMany(e => e.Vacations)
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserOnboardingState>()
            .HasOne(e => e.User)
            .WithOne(e => e.OnboardingState)
            .HasForeignKey<UserOnboardingState>(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserOnboardingState>()
            .HasIndex(e => e.UserId)
            .IsUnique();
    }
}
