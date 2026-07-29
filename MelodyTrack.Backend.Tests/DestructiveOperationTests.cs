using System.Net;
using System.Net.Http.Headers;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class DestructiveOperationTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Theory]
    [InlineData("/clients/")]
    [InlineData("/payments/")]
    [InlineData("/courses/")]
    public async Task DeletePrivilegedEntity_RegularUser_IsForbidden(string route)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        using var response = await App.Client.DeleteAsync(route + Ulid.NewUlid(), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DeleteClient_DeletesAuditsAndIsIdempotent()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Delete", "Client", TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        using var first = await App.Client.DeleteAsync($"/clients/{client.Id}", TestContext.Current.CancellationToken);
        using var second = await App.Client.DeleteAsync($"/clients/{client.Id}", TestContext.Current.CancellationToken);

        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        second.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        db.ChangeTracker.Clear();
        (await db.Clients.AnyAsync(item => item.Id == client.Id, TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await db.AuditLogs.CountAsync(
            item => item.Action == "client_deleted" && item.EntityId == client.Id.ToString(),
            TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task DeletePayment_RejectsStaleThenDeletesAuditsAndIsIdempotent()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Delete", "Payment", TestContext.Current.CancellationToken);
        var payment = new Payment
        {
            Id = Ulid.NewUlid(), Client = client, Amount = 100m,
            Date = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), Description = "Delete me"
        };
        var currentActivityId = Ulid.NewUlid();
        await db.Payments.AddAsync(payment, TestContext.Current.CancellationToken);
        await db.AuditLogs.AddAsync(new AuditLog
        {
            Id = currentActivityId, CreatedAtUtc = DateTime.UtcNow, Category = "payments",
            Action = "payment_created", EntityType = "payment", EntityId = payment.Id.ToString(), Details = "created"
        }, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        using var stale = await App.Client.DeleteAsync(
            $"/payments/{payment.Id}?expectedActivityId={Ulid.NewUlid()}",
            TestContext.Current.CancellationToken);
        using var deleted = await App.Client.DeleteAsync(
            $"/payments/{payment.Id}?expectedActivityId={currentActivityId}",
            TestContext.Current.CancellationToken);
        using var repeated = await App.Client.DeleteAsync($"/payments/{payment.Id}", TestContext.Current.CancellationToken);

        stale.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        repeated.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        db.ChangeTracker.Clear();
        (await db.AuditLogs.AnyAsync(
            item => item.Action == "payment_deleted" && item.EntityId == payment.Id.ToString(),
            TestContext.Current.CancellationToken)).ShouldBeTrue();
    }

    [Fact]
    public async Task DeleteCourse_RejectsAssignedCourseThenDeletesUnassignedCourseAndAudits()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Course", "Student", TestContext.Current.CancellationToken);
        var now = new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc);
        var course = new Course
        {
            Id = Ulid.NewUlid(), Name = "Disposable course", Description = "test", CreatedAtUtc = now, UpdatedAtUtc = now
        };
        var enrollment = new CourseEnrollment
        {
            Id = Ulid.NewUlid(), Client = client, ClientId = client.Id,
            Course = course, CourseId = course.Id, CreatedAtUtc = now, UpdatedAtUtc = now
        };
        await db.CourseEnrollments.AddAsync(enrollment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        using var assigned = await App.Client.DeleteAsync($"/courses/{course.Id}", TestContext.Current.CancellationToken);
        assigned.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        db.CourseEnrollments.Remove(enrollment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        using var deleted = await App.Client.DeleteAsync($"/courses/{course.Id}", TestContext.Current.CancellationToken);
        using var repeated = await App.Client.DeleteAsync($"/courses/{course.Id}", TestContext.Current.CancellationToken);

        deleted.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        repeated.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        db.ChangeTracker.Clear();
        (await db.Courses.AnyAsync(item => item.Id == course.Id, TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await db.AuditLogs.AnyAsync(
            item => item.Action == "course_deleted" && item.EntityId == course.Id.ToString(),
            TestContext.Current.CancellationToken)).ShouldBeTrue();
    }
}
