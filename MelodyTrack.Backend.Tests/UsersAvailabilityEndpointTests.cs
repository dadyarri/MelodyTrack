using System.Net;
using System.Net.Http.Headers;
using FastEndpoints;
using FastEndpoints.Testing;
using MelodyTrack.Backend.Api.Users.Endpoints;
using MelodyTrack.Backend.Api.Users.Responses;
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
public class UsersAvailabilityEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task GetUsersAvailability_ReturnsAllUsersAvailabilityForAdmin()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var teacher = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        var teacherWithoutCustomHours = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);

        await db.UserWorkingHoursDays.AddAsync(new UserWorkingHoursDay
        {
            Id = Ulid.NewUlid(),
            UserId = teacher.Id,
            User = teacher,
            DayOfWeek = DayOfWeek.Monday,
            IsWorkingDay = true,
            StartMinuteOfDay = 9 * 60,
            EndMinuteOfDay = 21 * 60
        }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var (response, content) = await App.Client.GETAsync<GetUsersAvailabilityEndpoint, EmptyRequest, GetUsersAvailabilityResponse>(
            EmptyRequest.Instance);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        content.ShouldNotBeNull();
        content.Availabilities.ShouldContain(item => item.UserId == teacher.Id && item.WorkingHours.Any(hour =>
            hour.DayOfWeek == "monday" && hour.StartTime == "09:00" && hour.EndTime == "21:00"));
        content.Availabilities.ShouldContain(item => item.UserId == teacherWithoutCustomHours.Id && item.WorkingHours.Count == 7);
    }

    [Fact]
    public async Task GetUsersAvailability_ReturnsForbiddenForRegularUser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.GetAsync("/users/availability", TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}
