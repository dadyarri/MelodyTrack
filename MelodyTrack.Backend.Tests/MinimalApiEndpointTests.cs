using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Releases.Endpoints;
using MelodyTrack.Backend.Api.Schedule.Endpoints;
using MelodyTrack.Backend.Api.Schedule.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public sealed class MinimalApiEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public void GeneratedEndpoints_HaveStableNamesAndNativeAuthorizationMetadata()
    {
        var endpoints = App.Services.GetRequiredService<EndpointDataSource>().Endpoints;

        var currentRelease = endpoints.Single(endpoint =>
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "GetCurrentRelease");
        currentRelease.Metadata.GetMetadata<IAllowAnonymous>().ShouldNotBeNull();

        var recurrenceTypes = endpoints.Single(endpoint =>
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName == "LookupRecurrenceTypes");
        recurrenceTypes.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldNotBeEmpty();
        recurrenceTypes.Metadata.GetMetadata<IAllowAnonymous>().ShouldBeNull();
    }

    [Fact]
    public async Task RecurrenceTypeLookup_UsesDefaultAuthorizationAndNativeBinding()
    {
        var unauthorized = await App.Client.GetAsync(
            "/appointment-recurrence-types/options",
            TestContext.Current.CancellationToken);
        unauthorized.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.GetAsync(
            "/appointment-recurrence-types/options",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var payload = await response.Content.ReadFromJsonAsync<LookupRecurrenceTypesResponse>(TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.RecurrenceTypes.Select(type => type.Key).ShouldBe(["daily", "monthly", "weekly"], ignoreOrder: true);
    }

    [Fact]
    public async Task RecurrenceTypeLookup_RejectsARevokedSession()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var session = new Session
        {
            Id = Ulid.NewUlid(),
            User = user,
            RefreshToken = "revoked-session-hash",
            ValidUntil = DateTime.UtcNow.AddHours(1),
            WasRevoked = true,
            DeviceInfo = "generator-integration-test"
        };
        await db.Sessions.AddAsync(session, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            UserUtils.CreateAccessToken(user, session.Id));

        var response = await App.Client.GetAsync(
            "/appointment-recurrence-types/options",
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}
