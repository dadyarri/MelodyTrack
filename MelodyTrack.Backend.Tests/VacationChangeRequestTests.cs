using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Vacations.Responses;
using MelodyTrack.Backend.Api.VacationRequests.Endpoints;
using MelodyTrack.Backend.Api.VacationRequests.Requests;
using MelodyTrack.Backend.Api.VacationRequests.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public sealed class VacationChangeRequestTests(MelodyTrackFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("update", false)]
    [InlineData("delete", false)]
    [InlineData("update", true)]
    [InlineData("delete", true)]
    public async Task Change_OwnVacation_AppliesOnlyAfterApproval(string action, bool clientOwner)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        Client? client = null;
        var actor = teacher;
        if (clientOwner)
        {
            client = await TestDataFactory.CreateClientAsync(db, "Portal", "Client", TestContext.Current.CancellationToken);
            actor = new User
            {
                Id = Ulid.NewUlid(), FirstName = "Portal", LastName = "User", Email = $"{Ulid.NewUlid()}@example.com", Password = "hash",
                PortalAlias = $"Test {client.Id}", ClientId = client.Id, Client = client, Role = await db.Roles.SingleAsync(item => item.RoleName == UserRoles.Client, TestContext.Current.CancellationToken)
            };
            db.Users.Add(actor);
        }
        var vacation = new Vacation { Id = Ulid.NewUlid(), UserId = clientOwner ? null : teacher.Id, ClientId = client?.Id,
            StartDate = new DateTime(2030, 6, 3, 9, 0, 0, DateTimeKind.Utc), EndDate = new DateTime(2030, 6, 7, 18, 0, 0, DateTimeKind.Utc) };
        db.Vacations.Add(vacation);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(actor));
        using var request = new HttpRequestMessage(action == "update" ? HttpMethod.Patch : HttpMethod.Delete,
            $"/vacations/{vacation.Id}{(action == "delete" ? "?expectedVersion=1" : "")}")
        {
            Content = JsonContent.Create(new { expectedVersion = 1, startDate = vacation.StartDate, endDate = vacation.EndDate.AddDays(-1) })
        };

        using var response = await App.Client.SendAsync(request, TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var result = await response.Content.ReadFromJsonAsync<VacationOperationResponse>(TestContext.Current.CancellationToken);

        result!.RequiresApproval.ShouldBeTrue();
        result.Request!.Action.ShouldBe(action);
        db.ChangeTracker.Clear();
        var unchanged = await db.Vacations.IgnoreQueryFilters().SingleAsync(item => item.Id == vacation.Id, TestContext.Current.CancellationToken);
        unchanged.EndDate.ShouldBe(vacation.EndDate);
        unchanged.Version.ShouldBe(1);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(superuser));

        var (approval, approved) = await App.Client.POSTAsync<ApproveVacationRequestEndpoint, VacationRequestDecisionRequest, VacationRequestResponse>(
            new VacationRequestDecisionRequest { Id = result.Request.Id, ExpectedVersion = result.Request.Version });

        approval.StatusCode.ShouldBe(HttpStatusCode.OK);
        approved.Status.ShouldBe("approved");
        db.ChangeTracker.Clear();
        var updated = await db.Vacations.IgnoreQueryFilters().SingleAsync(item => item.Id == vacation.Id, TestContext.Current.CancellationToken);
        updated.Id.ShouldBe(vacation.Id);
        updated.Version.ShouldBe(2);
        if (action == "delete") updated.IsCancelled.ShouldBeTrue();
        else updated.EndDate.ShouldBe(vacation.EndDate.AddDays(-1));
        (await db.AuditLogs.AnyAsync(item => item.EntityType == (clientOwner ? "client" : "user_availability")
            && item.EntityId == (clientOwner ? client!.Id : teacher.Id).ToString(), TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task Approve_AfterVacationVersionChanged_RejectsStaleRequest()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var superuser = await TestDataFactory.CreateSuperuserAsync(db, TestContext.Current.CancellationToken);
        var vacation = new Vacation { Id = Ulid.NewUlid(), UserId = teacher.Id,
            StartDate = new DateTime(2030, 6, 3, 9, 0, 0, DateTimeKind.Utc), EndDate = new DateTime(2030, 6, 7, 18, 0, 0, DateTimeKind.Utc) };
        db.Vacations.Add(vacation);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(teacher));
        using var response = await App.Client.DeleteAsync($"/vacations/{vacation.Id}?expectedVersion=1", TestContext.Current.CancellationToken);
        var operation = await response.Content.ReadFromJsonAsync<VacationOperationResponse>(TestContext.Current.CancellationToken);
        vacation.Version++;
        vacation.EndDate = vacation.EndDate.AddDays(1);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(superuser));

        var approval = await App.Client.POSTAsync<ApproveVacationRequestEndpoint, VacationRequestDecisionRequest>(
            new VacationRequestDecisionRequest { Id = operation!.Request!.Id, ExpectedVersion = operation.Request.Version });

        approval.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        db.ChangeTracker.Clear();
        var preserved = await db.Vacations.SingleAsync(item => item.Id == vacation.Id, TestContext.Current.CancellationToken);
        preserved.Version.ShouldBe(2);
        preserved.DeletedAtUtc.ShouldBeNull();
    }
}
