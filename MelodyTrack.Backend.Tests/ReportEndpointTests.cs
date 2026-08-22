using System.Net;
using System.Net.Http.Headers;
using MelodyTrack.Backend.Api.Reports.Endpoints;
using MelodyTrack.Backend.Api.Reports.Requests;
using MelodyTrack.Backend.Api.Reports.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public sealed class ReportEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task WorkReport_UsesTeacherScopeStatusesHistoricalPricesAvailabilityAndConsistentTotals()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var otherTeacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Anna", "Petrova", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Vocal", TestContext.Current.CancellationToken);
        var day = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        await db.ServicePriceHistory.AddRangeAsync(
        [
            new ServicePrice { Id = Ulid.NewUlid(), Service = service, Price = 100m, EffectiveDate = day.AddDays(-1) },
            new ServicePrice { Id = Ulid.NewUlid(), Service = service, Price = 150m, EffectiveDate = day.AddHours(10) }
        ], TestContext.Current.CancellationToken);
        await db.UserWorkingHoursDays.AddRangeAsync(
        [
            new UserWorkingHoursDay { Id = Ulid.NewUlid(), UserId = teacher.Id, User = teacher, DayOfWeek = DayOfWeek.Wednesday, IsWorkingDay = true, StartMinuteOfDay = 9 * 60, EndMinuteOfDay = 17 * 60 },
            new UserWorkingHoursDay { Id = Ulid.NewUlid(), UserId = teacher.Id, User = teacher, DayOfWeek = DayOfWeek.Thursday, IsWorkingDay = true, StartMinuteOfDay = 9 * 60, EndMinuteOfDay = 17 * 60 }
        ], TestContext.Current.CancellationToken);
        await db.UserVacations.AddAsync(new UserVacation
        {
            Id = Ulid.NewUlid(),
            UserId = teacher.Id,
            User = teacher,
            StartDate = new DateOnly(2026, 7, 2),
            EndDate = new DateOnly(2026, 7, 2)
        }, TestContext.Current.CancellationToken);
        await db.Appointments.AddRangeAsync(
        [
            Appointment(client, service, teacher, day.AddHours(9), AppointmentStatus.Completed),
            Appointment(client, service, teacher, day.AddHours(11), AppointmentStatus.Burned),
            Appointment(client, service, teacher, day.AddHours(12), AppointmentStatus.Cancelled),
            Appointment(client, service, teacher, day.AddHours(13), AppointmentStatus.Planned),
            Appointment(client, service, teacher, day.AddHours(14), AppointmentStatus.Completed, isDeleted: true),
            Appointment(client, service, otherTeacher, day.AddHours(15), AppointmentStatus.Completed),
            Appointment(client, service, teacher, day.AddDays(2), AppointmentStatus.Completed)
        ], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Authenticate(teacher);
        var request = Request(day, day.AddDays(1));
        request.ProviderId = teacher.Id;
        var (response, report) = await App.Client.GETAsync<GetWorkReportEndpoint, GetReportRequest, WorkReportResponse>(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        report.ShouldNotBeNull();
        report.Context.ProviderId.ShouldBe(teacher.Id);
        report.Context.ScopeLabel.ShouldBe("Viewer Admin");
        report.Summary.Appointments.ShouldBe(4);
        report.Summary.Completed.ShouldBe(1);
        report.Summary.Burned.ShouldBe(1);
        report.Summary.WorkingCapacityHours.ShouldBe(8m);
        report.Summary.OccupiedWorkingHours.ShouldBe(3m);
        report.Summary.FreeWorkingHours.ShouldBe(5m);
        report.Summary.UtilizationPercent.ShouldBe(37.5m);
        report.Summary.CancellationPercent.ShouldBe(25m);
        report.Statuses.Sum(item => item.Count).ShouldBe(report.Summary.Appointments);
        report.Trend.Sum(item => item.Appointments).ShouldBe(report.Summary.Appointments);
        report.Trend.Sum(item => item.OccupiedWorkingHours).ShouldBe(report.Summary.OccupiedWorkingHours);
        report.Trend.Sum(item => item.FreeWorkingHours).ShouldBe(report.Summary.FreeWorkingHours);
        report.Services.ShouldHaveSingleItem().Revenue.ShouldBe(250m);
        report.Providers.ShouldHaveSingleItem().ProviderId.ShouldBe(teacher.Id);
    }

    [Fact]
    public async Task WorkReport_UsesRequestedTimezoneAndMaterializesRecurringAppointments()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Timezone", "Client", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Piano", TestContext.Current.CancellationToken);
        var recurrenceType = await db.RecurrenceTypes.FirstAsync(type => type.Type == AppointmentRecurrenceType.Daily, TestContext.Current.CancellationToken);
        var utcBoundary = new DateTime(2026, 6, 30, 21, 30, 0, DateTimeKind.Utc);

        await db.ServicePriceHistory.AddAsync(new ServicePrice
        {
            Id = Ulid.NewUlid(), Service = service, Price = 100m, EffectiveDate = utcBoundary.AddDays(-1)
        }, TestContext.Current.CancellationToken);
        await db.Appointments.AddAsync(Appointment(client, service, teacher, utcBoundary, AppointmentStatus.Completed), TestContext.Current.CancellationToken);
        await db.RecurrenceRules.AddAsync(new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = teacher,
            StartDate = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc),
            RecurrenceType = recurrenceType,
            RecurrencePattern = 1
        }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Authenticate(teacher);
        var request = Request(new DateTime(2026, 7, 1), new DateTime(2026, 7, 1));
        request.Timezone = "Europe/Moscow";
        request.ProviderId = teacher.Id;
        var (response, report) = await App.Client.GETAsync<GetWorkReportEndpoint, GetReportRequest, WorkReportResponse>(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        report.ShouldNotBeNull();
        report.Summary.Appointments.ShouldBe(2);
        report.Statuses.Single(item => item.Status == "planned").Count.ShouldBe(1);
    }

    [Fact]
    public async Task FinanceReport_SeparatesPaymentsFromRevenueAndCalculatesOrganizationDebtAndProfit()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Finance", "Client", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Guitar", TestContext.Current.CancellationToken);
        var category = new ExpenseCategory { Id = Ulid.NewUlid(), Name = "Аренда" };
        var day = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);

        await db.ServicePriceHistory.AddAsync(new ServicePrice { Id = Ulid.NewUlid(), Service = service, Price = 100m, EffectiveDate = day.AddDays(-1) }, TestContext.Current.CancellationToken);
        await db.Appointments.AddRangeAsync(
        [
            Appointment(client, service, admin, day.AddHours(10), AppointmentStatus.Completed),
            Appointment(client, service, admin, day.AddHours(12), AppointmentStatus.Burned),
            Appointment(client, service, admin, day.AddHours(14), AppointmentStatus.Cancelled)
        ], TestContext.Current.CancellationToken);
        await db.Payments.AddAsync(new Payment { Id = Ulid.NewUlid(), Client = client, Service = service, Amount = 50m, Date = day.AddHours(15), Description = "Оплата" }, TestContext.Current.CancellationToken);
        await db.Expenses.AddAsync(new Expense { Id = Ulid.NewUlid(), Amount = 20m, Date = day.AddHours(16), Description = "Студия", Category = category, CategoryId = category.Id }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Authenticate(admin);
        var request = Request(day, day);
        var (response, report) = await App.Client.GETAsync<GetFinanceReportEndpoint, GetReportRequest, FinanceReportResponse>(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        report.ShouldNotBeNull();
        report.Summary.Revenue.ShouldBe(200m);
        report.Summary.Payments.ShouldBe(50m);
        report.Summary.Expenses.ShouldBe(20m);
        report.Summary.NetProfit.ShouldBe(180m);
        report.Summary.OutstandingDebt.ShouldBe(150m);
        report.Summary.AverageRevenuePerVisit.ShouldBe(100m);
        report.Trend.Sum(item => item.Revenue).ShouldBe(report.Summary.Revenue);
        report.Trend.Sum(item => item.Payments ?? 0m).ShouldBe(report.Summary.Payments!.Value);
        report.Trend.Sum(item => item.Expenses ?? 0m).ShouldBe(report.Summary.Expenses!.Value);
        report.Debtors.ShouldHaveSingleItem().Debt.ShouldBe(150m);
        report.ExpenseCategories.ShouldHaveSingleItem().CategoryName.ShouldBe("Аренда");

        request.ProviderId = admin.Id;
        var (_, selectedProvider) = await App.Client.GETAsync<GetFinanceReportEndpoint, GetReportRequest, FinanceReportResponse>(request);
        selectedProvider.ShouldNotBeNull();
        selectedProvider.Summary.Revenue.ShouldBe(200m);
        selectedProvider.Summary.OrganizationOnlyFiguresAvailable.ShouldBeFalse();
        selectedProvider.Summary.Payments.ShouldBeNull();
        selectedProvider.Summary.Expenses.ShouldBeNull();
        selectedProvider.Summary.NetProfit.ShouldBeNull();
        selectedProvider.Summary.OutstandingDebt.ShouldBeNull();
    }

    [Fact]
    public async Task ClientsReport_SeparatesAcquisitionRetentionRiskFrequencyAndValue()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Voice", TestContext.Current.CancellationToken);
        var source = new ClientSource { Id = Ulid.NewUlid(), Name = "Рекомендация" };
        var retained = await TestDataFactory.CreateClientAsync(db, "Retained", "Client", TestContext.Current.CancellationToken);
        var acquired = await TestDataFactory.CreateClientAsync(db, "New", "Client", TestContext.Current.CancellationToken);
        var risk = await TestDataFactory.CreateClientAsync(db, "Risk", "Client", TestContext.Current.CancellationToken);
        var lost = await TestDataFactory.CreateClientAsync(db, "Lost", "Client", TestContext.Current.CancellationToken);
        acquired.Source = source;
        acquired.SourceId = source.Id;
        var mayStart = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);

        await db.ServicePriceHistory.AddAsync(new ServicePrice { Id = Ulid.NewUlid(), Service = service, Price = 100m, EffectiveDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc) }, TestContext.Current.CancellationToken);
        await db.Appointments.AddRangeAsync(
        [
            Appointment(retained, service, teacher, new DateTime(2026, 4, 10, 10, 0, 0, DateTimeKind.Utc), AppointmentStatus.Completed),
            Appointment(retained, service, teacher, new DateTime(2026, 5, 10, 10, 0, 0, DateTimeKind.Utc), AppointmentStatus.Completed),
            Appointment(acquired, service, teacher, new DateTime(2026, 5, 15, 10, 0, 0, DateTimeKind.Utc), AppointmentStatus.Burned),
            Appointment(risk, service, teacher, new DateTime(2026, 4, 15, 10, 0, 0, DateTimeKind.Utc), AppointmentStatus.Completed),
            Appointment(lost, service, teacher, new DateTime(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc), AppointmentStatus.Completed),
            Appointment(acquired, service, teacher, new DateTime(2026, 5, 16, 10, 0, 0, DateTimeKind.Utc), AppointmentStatus.Cancelled),
            Appointment(acquired, service, teacher, new DateTime(2026, 5, 17, 10, 0, 0, DateTimeKind.Utc), AppointmentStatus.Completed, isDeleted: true)
        ], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Authenticate(teacher);
        var request = Request(mayStart, new DateTime(2026, 5, 31));
        request.ProviderId = teacher.Id;
        var (response, report) = await App.Client.GETAsync<GetClientsReportEndpoint, GetReportRequest, ClientsReportResponse>(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        report.ShouldNotBeNull();
        report.Summary.AcquiredClients.ShouldBe(1);
        report.Summary.ActiveClients.ShouldBe(2);
        report.Summary.RetainedClients.ShouldBe(1);
        report.Summary.RetentionPercent.ShouldBe(50m);
        report.Summary.AtRiskClients.ShouldBe(1);
        report.Summary.LostClients.ShouldBe(1);
        report.Summary.AverageVisitFrequency.ShouldBe(1m);
        report.Summary.AverageClientValue.ShouldBe(125m);
        report.Trend.Sum(item => item.Visits).ShouldBe(2);
        report.Sources.Single(item => item.SourceName == "Рекомендация").AcquiredClients.ShouldBe(1);
        report.Clients.Single(item => item.ClientId == risk.Id).ActivityState.ShouldBe("at-risk");
        report.Clients.Single(item => item.ClientId == lost.Id).ActivityState.ShouldBe("lost");
    }

    [Fact]
    public async Task Reports_AreLimitedToAdminsAndSuperusersAndAllowTeacherFiltering()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        var clientRole = await db.Roles.FirstAsync(role => role.RoleName == UserRoles.Client, TestContext.Current.CancellationToken);
        var clientUser = new User
        {
            Id = Ulid.NewUlid(),
            FirstName = "Portal",
            LastName = "Client",
            Email = $"{Ulid.NewUlid()}@example.com",
            Password = "hash",
            Role = clientRole
        };
        await db.Users.AddAsync(clientUser, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var request = Request(new DateTime(2026, 7, 1), new DateTime(2026, 7, 1));

        Authenticate(user);
        foreach (var path in new[] { "/reports/work", "/reports/finance", "/reports/clients" })
        {
            (await App.Client.GetAsync(
                $"{path}?start=2026-07-01&end=2026-07-01&timezone=UTC&groupBy=day",
                TestContext.Current.CancellationToken)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        }

        Authenticate(admin);
        var (adminResponse, organizationReport) = await App.Client.GETAsync<GetWorkReportEndpoint, GetReportRequest, WorkReportResponse>(request);
        adminResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        organizationReport.ShouldNotBeNull();
        organizationReport.Context.ScopeLabel.ShouldBe("Вся организация");
        request.ProviderId = user.Id;
        var (_, providerReport) = await App.Client.GETAsync<GetWorkReportEndpoint, GetReportRequest, WorkReportResponse>(request);
        providerReport.ShouldNotBeNull();
        providerReport.Context.ProviderId.ShouldBe(user.Id);
        providerReport.Context.ScopeLabel.ShouldBe("Operator Schedule");

        request.ProviderId = null;
        Authenticate(superuser);
        (await App.Client.GETAsync<GetWorkReportEndpoint, GetReportRequest>(request)).StatusCode.ShouldBe(HttpStatusCode.OK);

        Authenticate(clientUser);
        (await App.Client.GETAsync<GetWorkReportEndpoint, GetReportRequest>(request)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        Authenticate(superuser);
        request.End = request.Start.AddDays(732);
        (await App.Client.GETAsync<GetWorkReportEndpoint, GetReportRequest>(request)).StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ClientsReport_BoundsDetailedRowsForRepresentativeVolume()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Volume", TestContext.Current.CancellationToken);
        var start = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc);
        await db.ServicePriceHistory.AddAsync(new ServicePrice
        {
            Id = Ulid.NewUlid(), Service = service, Price = 100m, EffectiveDate = start.AddDays(-1)
        }, TestContext.Current.CancellationToken);

        var clients = Enumerable.Range(1, 125).Select(index => new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = $"Client {index}",
            LastName = "Volume",
            CreatedAtUtc = start.AddDays(-30),
            Contacts = new ClientContacts { Id = Ulid.NewUlid() }
        }).ToList();
        await db.Clients.AddRangeAsync(clients, TestContext.Current.CancellationToken);
        await db.Appointments.AddRangeAsync(
            clients.SelectMany((client, clientIndex) => Enumerable.Range(0, 8).Select(visitIndex =>
                Appointment(client, service, teacher, start.AddDays(visitIndex).AddMinutes(clientIndex), AppointmentStatus.Completed))),
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Authenticate(teacher);
        var request = Request(start.Date, start.Date.AddDays(7));
        request.ProviderId = teacher.Id;
        var (response, report) = await App.Client.GETAsync<GetClientsReportEndpoint, GetReportRequest, ClientsReportResponse>(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        report.ShouldNotBeNull();
        report.Summary.ActiveClients.ShouldBe(125);
        report.Trend.Sum(bucket => bucket.Visits).ShouldBe(1000);
        report.Clients.Count.ShouldBe(100);
    }

    [Fact]
    public async Task WorkReport_UsesAllTeachersAndUnionsOccupiedTimeInsideCapacity()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var teacherWithoutAppointments = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Capacity", "Client", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Capacity service", TestContext.Current.CancellationToken);
        var monday = new DateTime(2026, 7, 6, 0, 0, 0, DateTimeKind.Utc);

        await db.UserWorkingHoursDays.AddRangeAsync(
        [
            new UserWorkingHoursDay { Id = Ulid.NewUlid(), UserId = teacher.Id, User = teacher, DayOfWeek = DayOfWeek.Monday, IsWorkingDay = true, StartMinuteOfDay = 9 * 60, EndMinuteOfDay = 12 * 60 },
            new UserWorkingHoursDay { Id = Ulid.NewUlid(), UserId = teacher.Id, User = teacher, DayOfWeek = DayOfWeek.Tuesday, IsWorkingDay = true, StartMinuteOfDay = 9 * 60, EndMinuteOfDay = 12 * 60 }
        ], TestContext.Current.CancellationToken);
        await db.UserVacations.AddAsync(new UserVacation
        {
            Id = Ulid.NewUlid(), UserId = teacher.Id, User = teacher, StartDate = new DateOnly(2026, 7, 7), EndDate = new DateOnly(2026, 7, 7)
        }, TestContext.Current.CancellationToken);

        var first = Appointment(client, service, teacher, monday.AddHours(8.5), AppointmentStatus.Planned);
        first.EndDate = monday.AddHours(10.5);
        var overlapping = Appointment(client, service, teacher, monday.AddHours(9.5), AppointmentStatus.Completed);
        overlapping.EndDate = monday.AddHours(11.5);
        var outside = Appointment(client, service, teacher, monday.AddHours(11), AppointmentStatus.Burned);
        outside.EndDate = monday.AddHours(13);
        await db.Appointments.AddRangeAsync([first, overlapping, outside], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Authenticate(teacher);
        var (response, report) = await App.Client.GETAsync<GetWorkReportEndpoint, GetReportRequest, WorkReportResponse>(
            Request(monday, monday.AddDays(1)));

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        report.ShouldNotBeNull();
        report.Providers.ShouldContain(row => row.ProviderId == teacherWithoutAppointments.Id && row.Appointments == 0);
        var teacherRow = report.Providers.Single(row => row.ProviderId == teacher.Id);
        teacherRow.WorkingCapacityHours.ShouldBe(3m);
        teacherRow.OccupiedWorkingHours.ShouldBe(3m);
        teacherRow.FreeWorkingHours.ShouldBe(0m);
        teacherRow.UtilizationPercent.ShouldBe(100m);
        report.Providers.Sum(row => row.WorkingCapacityHours).ShouldBe(report.Summary.WorkingCapacityHours);
        report.Providers.Sum(row => row.OccupiedWorkingHours).ShouldBe(report.Summary.OccupiedWorkingHours);
        report.Summary.FreeWorkingHours.ShouldBe(report.Summary.WorkingCapacityHours - report.Summary.OccupiedWorkingHours);
    }

    [Fact]
    public async Task WorkReport_CalculatesElapsedCapacityAcrossDaylightSavingTransition()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        await db.UserWorkingHoursDays.AddAsync(new UserWorkingHoursDay
        {
            Id = Ulid.NewUlid(), UserId = teacher.Id, User = teacher, DayOfWeek = DayOfWeek.Sunday, IsWorkingDay = true, StartMinuteOfDay = 60, EndMinuteOfDay = 5 * 60
        }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Authenticate(teacher);
        var request = Request(new DateTime(2026, 10, 25), new DateTime(2026, 10, 25));
        request.ProviderId = teacher.Id;
        request.Timezone = "Europe/Berlin";
        var (_, report) = await App.Client.GETAsync<GetWorkReportEndpoint, GetReportRequest, WorkReportResponse>(request);

        report.ShouldNotBeNull();
        report.Summary.WorkingCapacityHours.ShouldBe(5m);
        report.Summary.FreeWorkingHours.ShouldBe(5m);
        report.Summary.UtilizationPercent.ShouldBe(0m);
    }

    [Fact]
    public async Task FinanceReport_UsesEarliestKnownPriceAndTotalsDebtBeyondDisplayedRows()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Historical price", TestContext.Current.CancellationToken);
        var day = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);
        await db.ServicePriceHistory.AddAsync(new ServicePrice
        {
            Id = Ulid.NewUlid(), Service = service, Price = 100m, EffectiveDate = day.AddDays(10)
        }, TestContext.Current.CancellationToken);
        var clients = Enumerable.Range(1, 101)
            .Select(index => new Client
            {
                Id = Ulid.NewUlid(), FirstName = $"Debtor {index}", LastName = "Client", CreatedAtUtc = day.AddDays(-1), Contacts = new ClientContacts { Id = Ulid.NewUlid() }
            })
            .ToList();
        await db.Clients.AddRangeAsync(clients, TestContext.Current.CancellationToken);
        await db.Appointments.AddRangeAsync(
            clients.Select((client, index) => Appointment(client, service, admin, day.AddHours(index % 20), AppointmentStatus.Completed)),
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Authenticate(admin);
        var (_, report) = await App.Client.GETAsync<GetFinanceReportEndpoint, GetReportRequest, FinanceReportResponse>(Request(day, day));

        report.ShouldNotBeNull();
        report.Summary.Revenue.ShouldBe(10100m);
        report.Summary.OutstandingDebt.ShouldBe(10100m);
        report.Debtors.Count.ShouldBe(100);
        report.Debtors.Sum(row => row.Debt).ShouldBe(10000m);
    }

    [Fact]
    public async Task ClientsReport_ClampsPartialBucketsAndPausesInactivityForVacations()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Client activity", TestContext.Current.CancellationToken);
        var returning = await TestDataFactory.CreateClientAsync(db, "Returning", "Client", TestContext.Current.CancellationToken);
        var vacation = await TestDataFactory.CreateClientAsync(db, "Vacation", "Client", TestContext.Current.CancellationToken);
        var paused = await TestDataFactory.CreateClientAsync(db, "Paused", "Client", TestContext.Current.CancellationToken);
        await db.ServicePriceHistory.AddAsync(new ServicePrice
        {
            Id = Ulid.NewUlid(), Service = service, Price = 100m, EffectiveDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
        }, TestContext.Current.CancellationToken);
        await db.Appointments.AddRangeAsync(
        [
            Appointment(returning, service, teacher, new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Utc), AppointmentStatus.Completed),
            Appointment(returning, service, teacher, new DateTime(2026, 5, 16, 10, 0, 0, DateTimeKind.Utc), AppointmentStatus.Completed),
            Appointment(vacation, service, teacher, new DateTime(2026, 3, 1, 10, 0, 0, DateTimeKind.Utc), AppointmentStatus.Completed),
            Appointment(paused, service, teacher, new DateTime(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc), AppointmentStatus.Completed)
        ], TestContext.Current.CancellationToken);
        await db.ClientVacations.AddRangeAsync(
        [
            new ClientVacation { Id = Ulid.NewUlid(), ClientId = vacation.Id, Client = vacation, StartDate = new DateOnly(2026, 5, 10), EndDate = new DateOnly(2026, 5, 31) },
            new ClientVacation { Id = Ulid.NewUlid(), ClientId = paused.Id, Client = paused, StartDate = new DateOnly(2026, 4, 10), EndDate = new DateOnly(2026, 4, 29) }
        ], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Authenticate(teacher);
        var request = Request(new DateTime(2026, 5, 15), new DateTime(2026, 5, 20));
        request.ProviderId = teacher.Id;
        request.GroupBy = "week";
        var (_, report) = await App.Client.GETAsync<GetClientsReportEndpoint, GetReportRequest, ClientsReportResponse>(request);

        report.ShouldNotBeNull();
        report.Summary.AcquiredClients.ShouldBe(0);
        report.Trend.Sum(bucket => bucket.AcquiredClients).ShouldBe(0);
        report.Trend.Sum(bucket => bucket.Visits).ShouldBe(1);
        report.Summary.OnVacationClients.ShouldBe(1);
        report.Clients.Single(row => row.ClientId == vacation.Id).ActivityState.ShouldBe("on-vacation");
        report.Clients.Single(row => row.ClientId == paused.Id).ActivityState.ShouldBe("inactive");
    }

    [Fact]
    public async Task Reports_ExcludeTrialServicesFromFinancialAndClientValue()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var regularClient = await TestDataFactory.CreateClientAsync(db, "Regular", "Client", TestContext.Current.CancellationToken);
        var trialClient = await TestDataFactory.CreateClientAsync(db, "Trial", "Lead", TestContext.Current.CancellationToken);
        var regularService = await TestDataFactory.CreateServiceAsync(db, "Regular lesson", TestContext.Current.CancellationToken);
        var trialService = await TestDataFactory.CreateServiceAsync(db, "Trial lesson", TestContext.Current.CancellationToken);
        trialService.IsConsultation = true;
        var day = new DateTime(2026, 7, 8, 0, 0, 0, DateTimeKind.Utc);

        await db.ServicePriceHistory.AddRangeAsync(
        [
            new ServicePrice { Id = Ulid.NewUlid(), Service = regularService, Price = 100m, EffectiveDate = day.AddDays(-1) },
            new ServicePrice { Id = Ulid.NewUlid(), Service = trialService, Price = 500m, EffectiveDate = day.AddDays(-1) }
        ], TestContext.Current.CancellationToken);
        await db.Appointments.AddRangeAsync(
        [
            Appointment(regularClient, regularService, teacher, day.AddHours(10), AppointmentStatus.Completed),
            Appointment(trialClient, trialService, teacher, day.AddHours(11), AppointmentStatus.Completed)
        ], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Authenticate(teacher);
        var request = Request(day, day);
        var (_, work) = await App.Client.GETAsync<GetWorkReportEndpoint, GetReportRequest, WorkReportResponse>(request);
        var (_, finance) = await App.Client.GETAsync<GetFinanceReportEndpoint, GetReportRequest, FinanceReportResponse>(request);
        var (_, clients) = await App.Client.GETAsync<GetClientsReportEndpoint, GetReportRequest, ClientsReportResponse>(request);

        work.ShouldNotBeNull();
        work.Summary.Appointments.ShouldBe(2);
        work.Summary.Completed.ShouldBe(2);
        work.Services.Single(row => row.ServiceId == trialService.Id).Revenue.ShouldBe(0m);

        finance.ShouldNotBeNull();
        finance.Summary.Revenue.ShouldBe(100m);
        finance.Summary.RevenueAppointments.ShouldBe(1);
        finance.Summary.AverageRevenuePerVisit.ShouldBe(100m);
        finance.Summary.OutstandingDebt.ShouldBe(100m);
        finance.Services.ShouldHaveSingleItem().ServiceId.ShouldBe(regularService.Id);
        finance.Debtors.ShouldHaveSingleItem().ClientId.ShouldBe(regularClient.Id);

        clients.ShouldNotBeNull();
        clients.Summary.AcquiredClients.ShouldBe(1);
        clients.Summary.ActiveClients.ShouldBe(1);
        clients.Summary.AverageClientValue.ShouldBe(100m);
        clients.Trend.Sum(row => row.Visits).ShouldBe(1);
        clients.Clients.ShouldHaveSingleItem().ClientId.ShouldBe(regularClient.Id);
    }

    private void Authenticate(User user)
    {
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
    }

    private static GetReportRequest Request(DateTime start, DateTime end) => new()
    {
        Start = start,
        End = end,
        Timezone = "UTC",
        GroupBy = "day"
    };

    private static Appointment Appointment(Client client, Service service, User provider, DateTime start, AppointmentStatus status, bool isDeleted = false) => new()
    {
        Id = Ulid.NewUlid(),
        Client = client,
        Service = service,
        Provider = provider,
        StartDate = DateTime.SpecifyKind(start, DateTimeKind.Utc),
        EndDate = DateTime.SpecifyKind(start.AddHours(1), DateTimeKind.Utc),
        Status = status,
        IsDeleted = isDeleted
    };
}
