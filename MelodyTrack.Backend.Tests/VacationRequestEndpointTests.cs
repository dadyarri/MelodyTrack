using System.Net;
using System.Net.Http.Headers;
using MelodyTrack.Backend.Api.VacationRequests.Endpoints;
using MelodyTrack.Backend.Api.VacationRequests.Requests;
using MelodyTrack.Backend.Api.VacationRequests.Responses;
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
public sealed class VacationRequestEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task CreateStaffRequest_TeacherSelfRequest_CreatesPendingRequestWithoutVacation()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = Bearer(teacher);

        var (response, result) = await App.Client.POSTAsync<CreateStaffVacationRequestEndpoint, CreateVacationRequest, VacationRequestResponse>(
            new CreateVacationRequest
            {
                StartDate = new DateOnly(2030, 6, 1),
                EndDate = new DateOnly(2030, 6, 7),
                Message = "Плановый отпуск"
            });

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        result.Status.ShouldBe("pending");
        result.SubjectId.ShouldBe(teacher.Id);
        (await db.UserVacations.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        (await db.Notifications.AnyAsync(item =>
            item.UserId == superuser.Id && item.ReferenceId == result.Id,
            TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task CreateClientRequest_PortalIdentity_DerivesClientAndKeepsRequestPrivate()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var firstClient = await TestDataFactory.CreateClientAsync(db, "Первый", "Клиент", TestContext.Current.CancellationToken);
        var secondClient = await TestDataFactory.CreateClientAsync(db, "Второй", "Клиент", TestContext.Current.CancellationToken);
        var firstPortalUser = await CreatePortalUserAsync(db, firstClient, TestContext.Current.CancellationToken);
        var secondPortalUser = await CreatePortalUserAsync(db, secondClient, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = Bearer(firstPortalUser);

        var (_, created) = await App.Client.POSTAsync<CreateClientVacationRequestEndpoint, CreateVacationRequest, VacationRequestResponse>(
            new CreateVacationRequest
            {
                StartDate = new DateOnly(2030, 7, 1),
                EndDate = new DateOnly(2030, 7, 5)
            });

        created.SubjectId.ShouldBe(firstClient.Id);
        App.Client.DefaultRequestHeaders.Authorization = Bearer(secondPortalUser);
        var (_, secondClientRequests) = await App.Client.GETAsync<GetClientVacationRequestsEndpoint, EmptyRequest, GetVacationRequestsResponse>(
            EmptyRequest.Instance);
        secondClientRequests.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReviewAndApprove_Superuser_CreatesVacationExactlyOnceAndNotifiesRequester()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var administrator = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = Bearer(administrator);
        var (_, created) = await App.Client.POSTAsync<CreateStaffVacationRequestEndpoint, CreateVacationRequest, VacationRequestResponse>(
            new CreateVacationRequest
            {
                StartDate = new DateOnly(2030, 8, 1),
                EndDate = new DateOnly(2030, 8, 10)
            });
        App.Client.DefaultRequestHeaders.Authorization = Bearer(superuser);

        var (queueResponse, queue) = await App.Client.GETAsync<GetVacationRequestReviewQueueEndpoint, GetVacationRequestsRequest, GetVacationRequestsResponse>(
            new GetVacationRequestsRequest { View = "pending" });
        var (approveResponse, approved) = await App.Client.POSTAsync<ApproveVacationRequestEndpoint, VacationRequestDecisionRequest, VacationRequestResponse>(
            new VacationRequestDecisionRequest
            {
                Id = created.Id,
                ExpectedVersion = created.Version,
                Message = "Согласовано"
            });
        var retryResponse = await App.Client.POSTAsync<ApproveVacationRequestEndpoint, VacationRequestDecisionRequest>(
            new VacationRequestDecisionRequest
            {
                Id = created.Id,
                ExpectedVersion = created.Version
            });

        queueResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        queue.Items.ShouldContain(item => item.Id == created.Id);
        approveResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        approved.Status.ShouldBe("approved");
        approved.ResultingVacationId.ShouldNotBeNull();
        retryResponse.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await db.UserVacations.CountAsync(item => item.UserId == administrator.Id, TestContext.Current.CancellationToken)).ShouldBe(1);
        (await db.Notifications.AnyAsync(item =>
            item.UserId == administrator.Id && item.Type == "vacation_request.approved",
            TestContext.Current.CancellationToken)).ShouldBeTrue();
        (await db.AuditLogs.AnyAsync(item =>
            item.Action == "vacation_request_approved" && item.EntityId == created.Id.ToString(),
            TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task Approve_RequestWithPlannedAppointment_ReturnsConflictWithoutChangingAppointment()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Занятый", "Клиент", TestContext.Current.CancellationToken);
        var service = await TestDataFactory.CreateServiceAsync(db, "Занятие", TestContext.Current.CancellationToken);
        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = teacher,
            StartDate = new DateTime(2030, 9, 3, 10, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2030, 9, 3, 11, 0, 0, DateTimeKind.Utc),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };
        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = Bearer(teacher);
        var (_, created) = await App.Client.POSTAsync<CreateStaffVacationRequestEndpoint, CreateVacationRequest, VacationRequestResponse>(
            new CreateVacationRequest
            {
                StartDate = new DateOnly(2030, 9, 1),
                EndDate = new DateOnly(2030, 9, 5)
            });
        App.Client.DefaultRequestHeaders.Authorization = Bearer(superuser);

        var response = await App.Client.POSTAsync<ApproveVacationRequestEndpoint, VacationRequestDecisionRequest>(
            new VacationRequestDecisionRequest { Id = created.Id, ExpectedVersion = created.Version });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await db.UserVacations.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        db.ChangeTracker.Clear();
        (await db.Appointments.SingleAsync(item => item.Id == appointment.Id, TestContext.Current.CancellationToken)).IsDeleted.ShouldBeFalse();
    }

    [Fact]
    public async Task DeclineAndCancel_PendingRequests_PreserveImmutableHistoryWithoutVacations()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = Bearer(teacher);
        var (_, declinedCandidate) = await App.Client.POSTAsync<CreateStaffVacationRequestEndpoint, CreateVacationRequest, VacationRequestResponse>(
            new CreateVacationRequest { StartDate = new DateOnly(2030, 10, 1), EndDate = new DateOnly(2030, 10, 2) });
        var (_, cancelledCandidate) = await App.Client.POSTAsync<CreateStaffVacationRequestEndpoint, CreateVacationRequest, VacationRequestResponse>(
            new CreateVacationRequest { StartDate = new DateOnly(2030, 11, 1), EndDate = new DateOnly(2030, 11, 2) });
        var cancelResponse = await App.Client.POSTAsync<CancelVacationRequestEndpoint, CancelVacationRequest>(
            new CancelVacationRequest { Id = cancelledCandidate.Id, ExpectedVersion = cancelledCandidate.Version });
        App.Client.DefaultRequestHeaders.Authorization = Bearer(superuser);
        var (declineResponse, declined) = await App.Client.POSTAsync<DeclineVacationRequestEndpoint, VacationRequestDecisionRequest, VacationRequestResponse>(
            new VacationRequestDecisionRequest
            {
                Id = declinedCandidate.Id,
                ExpectedVersion = declinedCandidate.Version,
                Message = "Период не согласован"
            });

        cancelResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        declineResponse.StatusCode.ShouldBe(HttpStatusCode.OK);
        declined.Status.ShouldBe("declined");
        (await db.VacationRequests.CountAsync(item => item.Status == VacationRequestStatus.Cancelled, TestContext.Current.CancellationToken)).ShouldBe(1);
        (await db.UserVacations.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Create_OverlappingPendingRequest_ReturnsConflict()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = Bearer(teacher);
        await App.Client.POSTAsync<CreateStaffVacationRequestEndpoint, CreateVacationRequest>(new CreateVacationRequest
        {
            StartDate = new DateOnly(2030, 12, 1),
            EndDate = new DateOnly(2030, 12, 10)
        });

        var response = await App.Client.POSTAsync<CreateStaffVacationRequestEndpoint, CreateVacationRequest>(new CreateVacationRequest
        {
            StartDate = new DateOnly(2030, 12, 5),
            EndDate = new DateOnly(2030, 12, 12)
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await db.VacationRequests.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task Create_EquivalentRetry_ReturnsOriginalRequestWithoutDuplicateNotification()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = Bearer(teacher);
        var input = new CreateVacationRequest
        {
            StartDate = new DateOnly(2031, 2, 1),
            EndDate = new DateOnly(2031, 2, 5),
            Message = "Повторяемый запрос"
        };

        var (_, first) = await App.Client.POSTAsync<CreateStaffVacationRequestEndpoint, CreateVacationRequest, VacationRequestResponse>(input);
        var (retryResponse, retry) = await App.Client.POSTAsync<CreateStaffVacationRequestEndpoint, CreateVacationRequest, VacationRequestResponse>(input);

        retryResponse.StatusCode.ShouldBe(HttpStatusCode.Created);
        retry.Id.ShouldBe(first.Id);
        (await db.VacationRequests.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await db.Notifications.CountAsync(item => item.UserId == superuser.Id, TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task Approve_Administrator_ReturnsForbidden()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var administrator = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = Bearer(teacher);
        var (_, created) = await App.Client.POSTAsync<CreateStaffVacationRequestEndpoint, CreateVacationRequest, VacationRequestResponse>(
            new CreateVacationRequest { StartDate = new DateOnly(2031, 1, 1), EndDate = new DateOnly(2031, 1, 2) });
        App.Client.DefaultRequestHeaders.Authorization = Bearer(administrator);

        var response = await App.Client.POSTAsync<ApproveVacationRequestEndpoint, VacationRequestDecisionRequest>(
            new VacationRequestDecisionRequest { Id = created.Id, ExpectedVersion = created.Version });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    private static AuthenticationHeaderValue Bearer(User user) =>
        new("Bearer", UserUtils.CreateAccessToken(user));

    private static async Task<User> CreatePortalUserAsync(AppDbContext db, Client client, CancellationToken ct)
    {
        var role = await db.Roles.SingleAsync(item => item.RoleName == UserRoles.Client, ct);
        var user = new User
        {
            Id = Ulid.NewUlid(),
            FirstName = client.FirstName,
            LastName = client.LastName,
            Email = $"{Ulid.NewUlid()}@portal.invalid",
            Password = "hash",
            Role = role,
            ClientId = client.Id
        };
        await db.Users.AddAsync(user, ct);
        await db.SaveChangesAsync(ct);
        return user;
    }
}
