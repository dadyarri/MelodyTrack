using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Clients.Responses;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class ClientEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task CreateClient_WithSourceAndContacts_PersistsNormalizedDataAndAudit()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var source = new ClientSource { Id = Ulid.NewUlid(), Name = "Recommendation" };
        await db.ClientSources.AddAsync(source, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        using var response = await App.Client.PostAsJsonAsync("/clients", new
        {
            firstName = "Anna",
            lastName = "Create",
            email = "ANNA.CREATE@example.com",
            phone = "+7 (999) 000-11-22",
            telegram = "@anna_create",
            sourceId = source.Id
        }, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var created = await response.Content.ReadFromJsonAsync<CreateEntityResponse>(TestContext.Current.CancellationToken);
        created.ShouldNotBeNull();
        db.ChangeTracker.Clear();
        var client = await db.Clients.Include(item => item.Contacts).Include(item => item.Source)
            .SingleAsync(item => item.Id == created.Id, TestContext.Current.CancellationToken);
        client.Contacts.Email.ShouldBe("anna.create@example.com");
        client.Contacts.Phone.ShouldBe("+7 (999) 000-11-22");
        client.Source!.Id.ShouldBe(source.Id);
        var auditCreated = await db.AuditLogs.AnyAsync(
            item => item.EntityId == client.Id.ToString() && item.Action == "client_created",
            TestContext.Current.CancellationToken);
        auditCreated.ShouldBeTrue();
    }

    [Theory]
    [InlineData("email", "duplicate@example.com", "DUPLICATE@example.com")]
    [InlineData("phone", "+7 (999) 123-45-67", "+79991234567")]
    [InlineData("telegram", "@duplicate_contact", " @DUPLICATE_CONTACT ")]
    [InlineData("vk", "https://vk.com/duplicate", " HTTPS://VK.COM/DUPLICATE ")]
    public async Task CreateClient_WithDuplicateContact_ReturnsConflict(string field, string storedValue, string requestedValue)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var existing = await TestDataFactory.CreateClientAsync(db, "Existing", "Contact", TestContext.Current.CancellationToken);
        SetContact(existing.Contacts, field, storedValue);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));
        var request = new Dictionary<string, object?>
        {
            ["firstName"] = "Duplicate",
            ["lastName"] = "Contact",
            [field] = requestedValue
        };

        using var response = await App.Client.PostAsJsonAsync("/clients", request, TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await response.Content.ReadFromJsonAsync<ApiProblemDetails>(TestContext.Current.CancellationToken);
        problem.ShouldNotBeNull();
        problem.Errors.ShouldContain(error => error.Path.Equals(field, StringComparison.OrdinalIgnoreCase));
        (await db.Clients.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task GetClients_SearchAndPagination_ReturnsMatchingOrderedPageAndTotals()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        await TestDataFactory.CreateClientAsync(db, "Alex", "Alpha", TestContext.Current.CancellationToken);
        await TestDataFactory.CreateClientAsync(db, "Alice", "Alpha", TestContext.Current.CancellationToken);
        await TestDataFactory.CreateClientAsync(db, "Boris", "Beta", TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        using var firstPageResponse = await App.Client.GetAsync(
            "/clients?search=Alpha&page=1&page_size=1",
            TestContext.Current.CancellationToken);
        using var secondPageResponse = await App.Client.GetAsync(
            "/clients?search=Alpha&page=2&page_size=1",
            TestContext.Current.CancellationToken);
        var firstPage = await firstPageResponse.Content.ReadFromJsonAsync<PaginatedResponse<ClientWithBalanceDto>>(TestContext.Current.CancellationToken);
        var secondPage = await secondPageResponse.Content.ReadFromJsonAsync<PaginatedResponse<ClientWithBalanceDto>>(TestContext.Current.CancellationToken);

        firstPageResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        secondPageResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        firstPage.ShouldNotBeNull();
        secondPage.ShouldNotBeNull();
        firstPage.Page.Total.ShouldBe(2);
        firstPage.Page.HasNextPage.ShouldBeTrue();
        firstPage.Page.HasPrevPage.ShouldBeFalse();
        secondPage.Page.HasNextPage.ShouldBeFalse();
        secondPage.Page.HasPrevPage.ShouldBeTrue();
        firstPage.Items.ShouldHaveSingleItem().FirstName.ShouldBe("Alex");
        secondPage.Items.ShouldHaveSingleItem().FirstName.ShouldBe("Alice");
    }

    [Fact]
    public async Task GetClients_LifecycleFilters_ReturnOnlyTheRequestedLifecycle()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var activeClient = await TestDataFactory.CreateClientAsync(db, "Active", "Client", TestContext.Current.CancellationToken);
        var lead = await TestDataFactory.CreateClientAsync(db, "Open", "Lead", TestContext.Current.CancellationToken);
        var thinking = await TestDataFactory.CreateClientAsync(db, "Thinking", "Lead", TestContext.Current.CancellationToken);
        var converted = await TestDataFactory.CreateClientAsync(db, "Converted", "Lead", TestContext.Current.CancellationToken);
        var closed = await TestDataFactory.CreateClientAsync(db, "Closed", "Lead", TestContext.Current.CancellationToken);
        closed.IsLeadClosed = true;
        var lesson = await TestDataFactory.CreateServiceAsync(db, "Lesson", TestContext.Current.CancellationToken);
        var consultation = await TestDataFactory.CreateServiceAsync(db, "Consultation", TestContext.Current.CancellationToken);
        consultation.IsConsultation = true;
        var past = new DateTime(2020, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        var future = new DateTime(2099, 1, 1, 10, 0, 0, DateTimeKind.Utc);
        await db.Appointments.AddRangeAsync(
        [
            CreateAppointment(activeClient, lesson, admin, future, AppointmentStatus.Planned),
            CreateAppointment(lead, consultation, admin, future, AppointmentStatus.Planned),
            CreateAppointment(thinking, consultation, admin, past, AppointmentStatus.Completed),
            CreateAppointment(converted, consultation, admin, past, AppointmentStatus.Completed),
            CreateAppointment(converted, lesson, admin, past.AddDays(1), AppointmentStatus.Completed)
        ], TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));
        var expected = new Dictionary<ClientLifecycleStatus, IReadOnlyCollection<Ulid>>
        {
            [ClientLifecycleStatus.Client] = [activeClient.Id, converted.Id],
            [ClientLifecycleStatus.Lead] = [lead.Id],
            [ClientLifecycleStatus.ThinkingLead] = [thinking.Id],
            [ClientLifecycleStatus.ClosedLead] = [closed.Id]
        };

        foreach (var (status, clientIds) in expected)
        {
            using var response = await App.Client.GetAsync(
                $"/clients?lifecycleStatus={(int)status}&page=1&page_size=20",
                TestContext.Current.CancellationToken);
            var page = await response.Content.ReadFromJsonAsync<PaginatedResponse<ClientWithBalanceDto>>(TestContext.Current.CancellationToken);
            response.StatusCode.ShouldBe(HttpStatusCode.OK);
            page.ShouldNotBeNull();
            page.Items.Select(client => client.Id).ShouldBe(clientIds, ignoreOrder: true);
        }
    }

    [Fact]
    public async Task CreateClient_RegularUser_IsForbidden()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        using var response = await App.Client.PostAsJsonAsync(
            "/clients",
            new { firstName = "Forbidden", lastName = "Client" },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateClientVacations_SuperuserDirectManagement_WritesNamedBeforeAndAfterAuditContext()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Иванова", TestContext.Current.CancellationToken);
        await db.ClientVacations.AddAsync(
            new ClientVacation
            {
                Id = Ulid.NewUlid(),
                ClientId = client.Id,
                Client = client,
                StartDate = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 8, 11, 0, 0, 0, DateTimeKind.Utc)
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));
        var response = await App.Client.PatchAsJsonAsync(
            $"/clients/{client.Id}",
            new
            {
                firstName = client.FirstName,
                lastName = client.LastName,
                vacations = new[]
                {
                    new { startDate = "2026-09-02T09:00:00Z", endDate = "2026-09-12T18:00:00Z" }
                }
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        db.ChangeTracker.Clear();
        var auditLog = await db.AuditLogs
            .AsNoTracking()
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstAsync(item => item.EntityId == client.Id.ToString(), TestContext.Current.CancellationToken);
        auditLog.Action.ShouldBe("client_vacations_updated_directly");
        auditLog.Details.ShouldBe(
            "Клиент: Иванова Анна; Периоды отсутствия: 2026-08-01 00:00–2026-08-11 00:00 UTC → 2026-09-02 09:00–2026-09-12 18:00 UTC");
    }

    [Fact]
    public async Task UpdateClientVacations_Administrator_ReturnsForbidden()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var administrator = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Анна", "Иванова", TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(administrator));

        var response = await App.Client.PatchAsJsonAsync(
            $"/clients/{client.Id}",
            new
            {
                firstName = client.FirstName,
                lastName = client.LastName,
                vacations = new[] { new { startDate = "2026-09-02T09:00:00Z", endDate = "2026-09-12T18:00:00Z" } }
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await db.ClientVacations.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    private static void SetContact(ClientContacts contacts, string field, string value)
    {
        switch (field)
        {
            case "email":
                contacts.Email = value;
                break;
            case "phone":
                contacts.Phone = value;
                break;
            case "telegram":
                contacts.Telegram = value;
                break;
            case "vk":
                contacts.Vk = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(field), field, null);
        }
    }

    private static Appointment CreateAppointment(
        Client client,
        Service service,
        User provider,
        DateTime start,
        AppointmentStatus status) => new()
        {
            Id = Ulid.NewUlid(), Client = client, Service = service, Provider = provider,
            StartDate = start, EndDate = start.AddHours(1), Status = status, IsDeleted = false
        };
}
