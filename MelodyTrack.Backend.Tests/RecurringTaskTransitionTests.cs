using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using MelodyTrack.Backend.Api.Tasks.Requests;
using MelodyTrack.Backend.Api.Tasks.Responses;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Services.RecurringTasks;
using MelodyTrack.Backend.Tests.Infrastructure;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

[Collection(IntegrationTestCollection.Name)]
public class RecurringTaskTransitionTests(MelodyTrackFixture app) : IntegrationTestBase(app)
{
    [Fact]
    public async Task CancelAsync_RecurringTask_CoversValidAlreadyProcessedAndStaleKey()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IRecurringTaskService>();
        var actor = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var client = await TestDataFactory.CreateClientAsync(db, "Cancel", "Recurring", TestContext.Current.CancellationToken);
        client.Contacts.Phone = "+79990000001";
        var appointmentService = await TestDataFactory.CreateServiceAsync(db, "Cancellation lesson", TestContext.Current.CancellationToken);
        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = appointmentService,
            StartDate = DateTime.UtcNow.AddHours(6),
            EndDate = DateTime.UtcNow.AddHours(7),
            Status = AppointmentStatus.Planned,
            IsDeleted = false
        };
        await db.Appointments.AddAsync(appointment, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var task = (await service.GetTasksAsync(
            "Europe/Moscow",
            RecurringTaskType.AppointmentReminder,
            RecurringTaskListStatus.Open,
            TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        var request = BuildCancelRequest(task);

        var valid = await service.CancelAsync(request, actor, TestContext.Current.CancellationToken);
        var alreadyProcessed = await service.CancelAsync(request, actor, TestContext.Current.CancellationToken);
        var stale = await service.CancelAsync(new CancelRecurringTaskRequest
        {
            Timezone = request.Timezone,
            RuleId = request.RuleId,
            Type = request.Type,
            DeduplicationKey = request.DeduplicationKey + ":stale",
            ClientId = request.ClientId,
            AppointmentId = request.AppointmentId
        }, actor, TestContext.Current.CancellationToken);

        valid.Succeeded.ShouldBeTrue();
        valid.Status.ShouldBe(RecurringTaskStatus.Cancelled);
        alreadyProcessed.Succeeded.ShouldBeFalse();
        alreadyProcessed.ErrorMessage.ShouldContain("уже обработана");
        stale.Succeeded.ShouldBeFalse();
        stale.ErrorMessage.ShouldContain("не актуальна");
    }

    [Fact]
    public async Task CancelAsync_CustomTask_CoversValidAlreadyProcessedAndStaleKey()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IRecurringTaskService>();
        var actor = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var task = CreateCustomTask(actor.Id);
        await db.CustomTasks.AddAsync(task, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var dto = (await service.GetTasksAsync(
            "Europe/Moscow",
            RecurringTaskType.CustomTask,
            RecurringTaskListStatus.Open,
            TestContext.Current.CancellationToken)).ShouldHaveSingleItem();
        var request = BuildCancelRequest(dto);

        var valid = await service.CancelAsync(request, actor, TestContext.Current.CancellationToken);
        var alreadyProcessed = await service.CancelAsync(request, actor, TestContext.Current.CancellationToken);
        var staleTask = CreateCustomTask(actor.Id);
        await db.CustomTasks.AddAsync(staleTask, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var stale = await service.CancelAsync(new CancelRecurringTaskRequest
        {
            Timezone = "Europe/Moscow",
            RuleId = staleTask.Id,
            Type = RecurringTaskType.CustomTask.ToApiKey(),
            DeduplicationKey = "custom-task:stale"
        }, actor, TestContext.Current.CancellationToken);

        valid.Succeeded.ShouldBeTrue();
        alreadyProcessed.Succeeded.ShouldBeFalse();
        alreadyProcessed.ErrorMessage.ShouldContain("уже обработана");
        stale.Succeeded.ShouldBeFalse();
        stale.ErrorMessage.ShouldContain("не актуальна");
    }

    [Theory]
    [InlineData("appointment-reminder")]
    [InlineData("custom-task")]
    public async Task CancelEndpoint_RegularUser_IsForbidden(string type)
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await TestDataFactory.CreateAuthorizedScheduleUserAsync(db, TestContext.Current.CancellationToken);
        App.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", UserUtils.CreateAccessToken(user));

        using var response = await App.Client.PostAsJsonAsync(
            "/tasks/stale-key/cancellation",
            new
            {
                timezone = "Europe/Moscow",
                ruleId = Ulid.NewUlid(),
                type,
                deduplicationKey = "stale-key"
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task CancelAsync_RecurringTask_WhenAuditPersistenceFails_RollsBackExecution()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actor = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var rule = await db.RecurringTaskRules.FirstAsync(TestContext.Current.CancellationToken);
        var candidate = new RecurringTaskCandidate
        {
            RuleId = rule.Id,
            Type = rule.Type,
            RecipientType = RecurringTaskRecipientType.External,
            DeduplicationKey = "persistence-failure",
            Title = "Persistence failure",
            RelatedPersonDisplayName = "External recipient",
            BusinessDate = DateOnly.FromDateTime(DateTime.UtcNow),
            PreparedMessage = "Message",
            SortAtUtc = DateTime.UtcNow
        };
        var transition = new RecurringTaskTransitionService(
            db,
            new ThrowingAuditLogService(),
            TimeProvider.System,
            new UnusedCustomTaskTransitionService(),
            new FixedCandidateService(candidate));
        var request = new CancelRecurringTaskRequest
        {
            Timezone = "Europe/Moscow",
            RuleId = rule.Id,
            Type = rule.Type.ToApiKey(),
            DeduplicationKey = candidate.DeduplicationKey
        };

        await Should.ThrowAsync<InvalidOperationException>(() =>
            transition.CancelAsync(request, actor, TestContext.Current.CancellationToken));

        await using var assertionScope = App.Services.CreateAsyncScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await assertionDb.RecurringTaskExecutions
            .AnyAsync(item => item.DeduplicationKey == candidate.DeduplicationKey, TestContext.Current.CancellationToken);
        persisted.ShouldBeFalse();
    }

    [Fact]
    public async Task CancelAsync_CustomTask_WhenAuditPersistenceFails_RollsBackTaskState()
    {
        await using var scope = App.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actor = await TestDataFactory.CreateAdminUserAsync(db, TestContext.Current.CancellationToken);
        var task = CreateCustomTask(actor.Id);
        await db.CustomTasks.AddAsync(task, TestContext.Current.CancellationToken);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        var transition = new CustomTaskTransitionService(db, new ThrowingAuditLogService(), TimeProvider.System);
        var request = new CancelRecurringTaskRequest
        {
            Timezone = "Europe/Moscow",
            RuleId = task.Id,
            Type = RecurringTaskType.CustomTask.ToApiKey(),
            DeduplicationKey = RecurringTaskPresentationMapper.BuildCustomTaskDeduplicationKey(task.Id)
        };

        await Should.ThrowAsync<InvalidOperationException>(() =>
            transition.CancelAsync(request, actor, TestContext.Current.CancellationToken));

        await using var assertionScope = App.Services.CreateAsyncScope();
        var assertionDb = assertionScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await assertionDb.CustomTasks
            .AsNoTracking()
            .SingleAsync(item => item.Id == task.Id, TestContext.Current.CancellationToken);
        persisted.CancelledAtUtc.ShouldBeNull();
        persisted.CancelledByUserId.ShouldBeNull();
    }

    private static CancelRecurringTaskRequest BuildCancelRequest(RecurringTaskDto task) => new()
    {
        Timezone = "Europe/Moscow",
        RuleId = task.RuleId,
        Type = task.Type,
        DeduplicationKey = task.DeduplicationKey,
        ClientId = task.ClientId,
        TeacherId = task.TeacherId,
        AppointmentId = task.AppointmentId
    };

    private static CustomTask CreateCustomTask(Ulid actorId) => new()
    {
        Id = Ulid.NewUlid(),
        RecipientName = "External recipient",
        Title = "Call recipient",
        MessageText = "Message",
        DueAtUtc = DateTime.UtcNow.AddHours(1),
        CreatedAtUtc = DateTime.UtcNow,
        CreatedByUserId = actorId
    };

    private sealed class ThrowingAuditLogService : IAuditLogService
    {
        public Task WriteAsync(AuditLogWriteRequest request, CancellationToken ct) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }

    private sealed class FixedCandidateService(RecurringTaskCandidate candidate) : IRecurringTaskCandidateService
    {
        public Task<List<RecurringTaskDto>> GetOpenTasksAsync(string timezone, RecurringTaskType? filterType, CancellationToken ct) =>
            Task.FromResult(new List<RecurringTaskDto>());

        public Task<RecurringTaskCandidate?> FindCandidateAsync(
            string timezone,
            Ulid ruleId,
            string deduplicationKey,
            string typeApiKey,
            Ulid? clientId,
            Ulid? teacherId,
            Ulid? appointmentId,
            CancellationToken ct) => Task.FromResult<RecurringTaskCandidate?>(candidate);
    }

    private sealed class UnusedCustomTaskTransitionService : ICustomTaskTransitionService
    {
        public Task<RecurringTaskActionResult> CompleteAsync(CompleteRecurringTaskRequest request, User actor, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RecurringTaskActionResult> CancelAsync(CancelRecurringTaskRequest request, User actor, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<RecurringTaskActionResult> DelayAsync(DelayRecurringTaskRequest request, DateTime delayUntilUtc, User actor, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
