using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Tasks.Responses;
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
public class RecurringTaskRuleEndpointTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task GetRecurringTaskRules_ReturnsSeededRulesForAdmin()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var response = await App.Client.GetAsync("/tasks/rules", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await response.Content.ReadFromJsonAsync<GetRecurringTaskRulesResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.Rules.Count.ShouldBeGreaterThanOrEqualTo(5);
        payload.Rules.ShouldContain(rule => rule.Type == "appointment-reminder");
        payload.Rules.ShouldContain(rule => rule.Type == "teacher-daily-schedule");
    }

    [Fact]
    public async Task GetRecurringTaskRules_ReturnsForbiddenForRegularUser()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        var response = await App.Client.GetAsync("/tasks/rules", TestContext.Current.CancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UpdateRecurringTaskRule_UpdatesEditableFields()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var rule = await db.RecurringTaskRules.FirstAsync(item => item.Type == RecurringTaskType.InactiveClientReminder, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var response = await App.Client.PutAsJsonAsync(
            $"/tasks/rules/{rule.Id}",
            new
            {
                isEnabled = false,
                messageTemplate = "Новый шаблон для возврата клиента",
                offsetMinutes = (int?)null,
                cooldownDays = 14
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        db.ChangeTracker.Clear();
        var updatedRule = await db.RecurringTaskRules.AsNoTracking().FirstAsync(item => item.Id == rule.Id, TestContext.Current.CancellationToken);
        updatedRule.IsEnabled.ShouldBeFalse();
        updatedRule.MessageTemplate.ShouldBe("Новый шаблон для возврата клиента");
        updatedRule.CooldownDays.ShouldBe(14);
        updatedRule.OffsetMinutes.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateRecurringTaskRule_ReturnsConflictWhenExpectedActivityIdIsStale()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var rule = await db.RecurringTaskRules.FirstAsync(item => item.Type == RecurringTaskType.AppointmentReminder, TestContext.Current.CancellationToken);
        var activityId = Ulid.NewUlid();

        await db.AuditLogs.AddAsync(
            new AuditLog
            {
                Id = activityId,
                CreatedAtUtc = DateTime.UtcNow,
                Category = "recurring_tasks",
                Action = "recurring_task_rule_updated",
                EntityType = "recurring_task_rule",
                EntityId = rule.Id.ToString(),
                Details = "Rule updated"
            },
            TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var response = await App.Client.PutAsJsonAsync(
            $"/tasks/rules/{rule.Id}",
            new
            {
                isEnabled = rule.IsEnabled,
                messageTemplate = $"{rule.MessageTemplate}!",
                offsetMinutes = rule.OffsetMinutes,
                cooldownDays = rule.CooldownDays,
                expectedActivityId = Ulid.NewUlid()
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var payload = await response.Content.ReadFromJsonAsync<StaleEntityConflictResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.EntityType.ShouldBe("recurring_task_rule");
        payload.CurrentActivity.ShouldNotBeNull();
        payload.CurrentActivity.Id.ShouldBe(activityId);
    }

    [Fact]
    public async Task UpdateRecurringTaskRule_AffectsGeneratedTaskMessage()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var admin = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Иван", "Петров", TestContext.Current.CancellationToken);
        client.Contacts.Phone = "+79991234567";

        var service = await TestDataFactory.CreateServiceAsync(db, "Математика", TestContext.Current.CancellationToken);
        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = DateTime.UtcNow.AddHours(12),
            EndDate = DateTime.UtcNow.AddHours(13),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };

        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var rule = await db.RecurringTaskRules.FirstAsync(item => item.Type == RecurringTaskType.AppointmentReminder, TestContext.Current.CancellationToken);

        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(admin));

        var updateResponse = await App.Client.PutAsJsonAsync(
            $"/tasks/rules/{rule.Id}",
            new
            {
                isEnabled = rule.IsEnabled,
                messageTemplate = "Кастом: {Client.FirstName}, урок {When} в {Appointment.StartTime}",
                offsetMinutes = rule.OffsetMinutes,
                cooldownDays = rule.CooldownDays
            },
            TestContext.Current.CancellationToken);

        updateResponse.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        var dueResponse = await App.Client.GetAsync("/tasks/due?timezone=Europe/Moscow&type=appointment-reminder&status=open", TestContext.Current.CancellationToken);
        dueResponse.StatusCode.ShouldBe(HttpStatusCode.OK);

        var payload = await dueResponse.Content.ReadFromJsonAsync<GetDueRecurringTasksResponse>(cancellationToken: TestContext.Current.CancellationToken);
        payload.ShouldNotBeNull();
        payload.Tasks.ShouldContain(task => task.PreparedMessage.Contains("Кастом: Иван", StringComparison.Ordinal));
    }
}
