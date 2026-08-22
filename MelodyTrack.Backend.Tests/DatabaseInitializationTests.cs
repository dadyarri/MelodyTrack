using System.Net;
using MelodyTrack.Backend.Api.Auth.Endpoints;
using MelodyTrack.Backend.Api.Auth.Requests;
using MelodyTrack.Backend.Api.Auth.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Data.Initialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OtpNet;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public sealed class DatabaseInitializationTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    private const string DevelopmentEmail = "dev.superuser@melodytrack.local";
    private const string DevelopmentPassword = "MelodyTrack-Development-Only!";
    private const string DevelopmentTotpSecret = "JBSWY3DPEHPK3PXPJBSWY3DPEHPK3PXP";

    [Fact]
    public async Task DevelopmentMode_IsRepeatableAndCreatesUsableDemoEnvironment()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var initializationStartedUtc = DateTime.UtcNow;

        await App.RunInitializationAsync(InitializationMode.Development, cancellationToken);
        await App.RunInitializationAsync(InitializationMode.Development, cancellationToken);

        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var versionOneMarkerCount = await db.AuditLogs
            .AsNoTracking()
            .CountAsync(log => log.Action == "development_seed_v1", cancellationToken);
        var versionTwoMarkerCount = await db.AuditLogs
            .AsNoTracking()
            .CountAsync(log => log.Action == "development_seed_v2", cancellationToken);
        var versionThreeMarkerCount = await db.AuditLogs
            .AsNoTracking()
            .CountAsync(log => log.Action == "development_seed_v3", cancellationToken);
        var versionFourMarkerCount = await db.AuditLogs
            .AsNoTracking()
            .CountAsync(log => log.Action == "development_seed_v4", cancellationToken);
        var versionFiveMarkerCount = await db.AuditLogs
            .AsNoTracking()
            .CountAsync(log => log.Action == "development_seed_v5", cancellationToken);
        var versionSixMarkerCount = await db.AuditLogs
            .AsNoTracking()
            .CountAsync(log => log.Action == "development_seed_v6", cancellationToken);
        var demoAppointments = await db.Appointments
            .AsNoTracking()
            .Include(appointment => appointment.Client)
            .Include(appointment => appointment.Provider)
            .Where(appointment => appointment.LessonNotes != null && appointment.LessonNotes.StartsWith("[demo-v3]"))
            .ToListAsync(cancellationToken);
        var demoPaymentCount = await db.Payments
            .AsNoTracking()
            .CountAsync(payment => payment.Description.StartsWith("[demo-v3]"), cancellationToken);
        var demoExpenseCount = await db.Expenses
            .AsNoTracking()
            .CountAsync(expense => expense.Description.StartsWith("[demo-v3]"), cancellationToken);
        var upcomingAppointments = await db.Appointments
            .AsNoTracking()
            .Include(appointment => appointment.Provider)
            .Where(appointment => appointment.LessonNotes == "[demo-v5]")
            .ToListAsync(cancellationToken);
        var clients = await db.Clients
            .AsNoTracking()
            .Include(client => client.Contacts)
            .ToListAsync(cancellationToken);
        var appointmentStatuses = await db.Appointments
            .AsNoTracking()
            .GroupBy(appointment => appointment.Status)
            .ToDictionaryAsync(group => group.Key, group => group.Count(), cancellationToken);
        var deletedAppointmentCount = await db.Appointments
            .AsNoTracking()
            .CountAsync(appointment => appointment.IsDeleted, cancellationToken);
        var unassignedAppointmentCount = await db.Appointments
            .AsNoTracking()
            .CountAsync(appointment => appointment.Provider == null, cancellationToken);
        var detailedLessonNoteCount = await db.Appointments
            .AsNoTracking()
            .CountAsync(
                appointment => appointment.LessonNotes != null
                    && !appointment.LessonNotes.StartsWith("[demo-v3]")
                    && appointment.LessonNotes != "[demo-v5]",
                cancellationToken);
        var prepaymentCount = await db.Payments
            .AsNoTracking()
            .CountAsync(payment => payment.Description == "Предоплата занятия", cancellationToken);
        var studioRentCount = await db.Expenses
            .AsNoTracking()
            .CountAsync(expense => expense.Description == "Аренда студии", cancellationToken);
        var weeklyRecurrenceRuleCount = await db.RecurrenceRules
            .AsNoTracking()
            .CountAsync(
                rule => rule.RecurrenceType.Type == AppointmentRecurrenceType.Weekly,
                cancellationToken);
        var priceHistoryCount = await db.ServicePriceHistory
            .AsNoTracking()
            .CountAsync(cancellationToken);
        var developmentUser = await db.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == Ulid.Parse("01K00000000000000000000001"), cancellationToken);

        versionOneMarkerCount.ShouldBe(1);
        versionTwoMarkerCount.ShouldBe(1);
        versionThreeMarkerCount.ShouldBe(1);
        versionFourMarkerCount.ShouldBe(1);
        versionFiveMarkerCount.ShouldBe(1);
        versionSixMarkerCount.ShouldBe(1);
        demoAppointments.Count.ShouldBeGreaterThanOrEqualTo(240);
        demoAppointments.Min(appointment => appointment.StartDate)
            .ShouldBeGreaterThanOrEqualTo(initializationStartedUtc.AddMonths(-6).Date);
        demoAppointments.Max(appointment => appointment.StartDate).ShouldBeLessThan(DateTime.UtcNow);
        demoAppointments.ShouldAllBe(appointment => appointment.Client.CreatedAtUtc <= appointment.StartDate);
        demoAppointments.ShouldAllBe(
            appointment => appointment.Provider != null
                && appointment.Provider.Id == Ulid.Parse("01K00000000000000000000001"));
        demoPaymentCount.ShouldBeGreaterThanOrEqualTo(190);
        demoExpenseCount.ShouldBeGreaterThanOrEqualTo(20);
        upcomingAppointments.Count.ShouldBeGreaterThanOrEqualTo(28);
        upcomingAppointments.ShouldAllBe(appointment => appointment.Status == AppointmentStatus.Planned);
        upcomingAppointments.ShouldAllBe(appointment => appointment.StartDate > initializationStartedUtc);
        upcomingAppointments.ShouldAllBe(
            appointment => appointment.Provider != null
                && appointment.Provider.Id == Ulid.Parse("01K00000000000000000000001"));
        clients.Count.ShouldBeGreaterThanOrEqualTo(49);
        clients.Count(client => client.Contacts.Telegram != null && client.Contacts.Vk != null)
            .ShouldBeGreaterThanOrEqualTo(48);
        appointmentStatuses[AppointmentStatus.Completed].ShouldBeGreaterThan(0);
        appointmentStatuses[AppointmentStatus.Cancelled].ShouldBeGreaterThan(0);
        appointmentStatuses[AppointmentStatus.Burned].ShouldBeGreaterThan(0);
        appointmentStatuses[AppointmentStatus.Planned].ShouldBeGreaterThan(0);
        deletedAppointmentCount.ShouldBeGreaterThan(0);
        unassignedAppointmentCount.ShouldBe(0);
        detailedLessonNoteCount.ShouldBeGreaterThan(0);
        prepaymentCount.ShouldBeGreaterThan(0);
        studioRentCount.ShouldBeGreaterThanOrEqualTo(6);
        weeklyRecurrenceRuleCount.ShouldBeGreaterThanOrEqualTo(6);
        priceHistoryCount.ShouldBeGreaterThanOrEqualTo(15);
        developmentUser.TotpSecret.ShouldBe(DevelopmentTotpSecret);

        var totp = new Totp(Base32Encoding.ToBytes(DevelopmentTotpSecret), mode: OtpHashMode.Sha1);
        var (response, result) = await App.Client.POSTAsync<LoginEndpoint, LoginRequest, LoginResponse>(new LoginRequest
        {
            Email = DevelopmentEmail,
            Password = DevelopmentPassword,
            Otp = totp.ComputeTotp()
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        result.ShouldNotBeNull();
        result.AccessToken.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DevelopmentMode_UpgradesVersionOneIdentityWithMissingSecondFactor()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await App.RunInitializationAsync(InitializationMode.Development, cancellationToken);

        await using (var scope = App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var versionTwoMarker = await db.AuditLogs
                .SingleAsync(log => log.Action == "development_seed_v2", cancellationToken);
            var developmentUser = await db.Users
                .SingleAsync(user => user.Id == Ulid.Parse("01K00000000000000000000001"), cancellationToken);

            db.AuditLogs.Remove(versionTwoMarker);
            developmentUser.TotpSecret = null;
            await db.SaveChangesAsync(cancellationToken);
        }

        await App.RunInitializationAsync(InitializationMode.Development, cancellationToken);

        await using var assertionScope = App.Services.CreateAsyncScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var upgradedUser = await assertionDb.Users
            .AsNoTracking()
            .SingleAsync(user => user.Id == Ulid.Parse("01K00000000000000000000001"), cancellationToken);
        var markerCount = await assertionDb.AuditLogs
            .AsNoTracking()
            .CountAsync(log => log.Action == "development_seed_v2", cancellationToken);

        upgradedUser.TotpSecret.ShouldBe(DevelopmentTotpSecret);
        markerCount.ShouldBe(1);
    }

    [Fact]
    public async Task DevelopmentMode_RepairsMissingDemoAppointmentProviders()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await App.RunInitializationAsync(InitializationMode.Development, cancellationToken);

        await using (var scope = App.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var versionFourMarker = await db.AuditLogs
                .SingleAsync(log => log.Action == "development_seed_v4", cancellationToken);
            var demoAppointments = await db.Appointments
                .Where(appointment => appointment.LessonNotes != null && appointment.LessonNotes.StartsWith("[demo-v3]"))
                .ToListAsync(cancellationToken);

            db.AuditLogs.Remove(versionFourMarker);
            foreach (var appointment in demoAppointments)
            {
                appointment.Provider = null;
            }

            await db.SaveChangesAsync(cancellationToken);
        }

        await App.RunInitializationAsync(InitializationMode.Development, cancellationToken);

        await using var assertionScope = App.Services.CreateAsyncScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unassignedAppointmentCount = await assertionDb.Appointments
            .AsNoTracking()
            .CountAsync(
                appointment => appointment.LessonNotes != null
                    && appointment.LessonNotes.StartsWith("[demo-v3]")
                    && (appointment.Provider == null
                        || appointment.Provider.Id != Ulid.Parse("01K00000000000000000000001")),
                cancellationToken);
        var markerCount = await assertionDb.AuditLogs
            .AsNoTracking()
            .CountAsync(log => log.Action == "development_seed_v4", cancellationToken);

        unassignedAppointmentCount.ShouldBe(0);
        markerCount.ShouldBe(1);
    }
}
