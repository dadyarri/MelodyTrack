using MelodyTrack.Backend.Api.WorkingHoursRequests.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Notifications;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public sealed record WorkingHoursRequestWorkflowResult(
    VacationRequestWorkflowFailure Failure,
    string? Detail = null,
    WorkingHoursRequest? Request = null);

public interface IWorkingHoursRequestWorkflowService
{
    Task<WorkingHoursRequestWorkflowResult> CreateAsync(User requester, CreateWorkingHoursRequest input, CancellationToken ct);
    Task<WorkingHoursRequestWorkflowResult> ApproveAsync(Ulid requestId, int expectedVersion, string? decisionMessage, User superuser, CancellationToken ct);
    Task<WorkingHoursRequestWorkflowResult> DeclineAsync(Ulid requestId, int expectedVersion, string? decisionMessage, User superuser, CancellationToken ct);
    Task<WorkingHoursRequestWorkflowResult> CancelAsync(Ulid requestId, int expectedVersion, User requester, CancellationToken ct);
}

public sealed class WorkingHoursRequestWorkflowService(
    AppDbContext db,
    INotificationService notificationService,
    IAuditLogService auditLogService,
    IVacationRequestSubjectLock subjectLock,
    TimeProvider timeProvider) : IWorkingHoursRequestWorkflowService
{
    public async Task<WorkingHoursRequestWorkflowResult> CreateAsync(
        User requester,
        CreateWorkingHoursRequest input,
        CancellationToken ct)
    {
        if (requester.Role.RoleName.IsClient() || requester.Role.RoleName.IsSuperuser() ||
            (requester.Role.RoleName & (UserRoles.User | UserRoles.Admin)) == 0)
        {
            return new WorkingHoursRequestWorkflowResult(
                VacationRequestWorkflowFailure.Forbidden,
                "Заявки на изменение собственных рабочих дней доступны преподавателям и администраторам.");
        }

        var requestedDays = MapInput(input.WorkingHours);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await subjectLock.AcquireWorkingHoursAsync(requester.Id, ct);

        var pending = await db.WorkingHoursRequests
            .AsNoTracking()
            .Include(item => item.RequestedWorkingHours)
            .SingleOrDefaultAsync(item =>
                item.SubjectUserId == requester.Id && item.Status == VacationRequestStatus.Pending, ct);
        if (pending is not null)
        {
            if (pending.RequesterUserId == requester.Id &&
                pending.RequestMessage == NormalizeMessage(input.Message) &&
                SchedulesEqual(pending.RequestedWorkingHours, requestedDays))
            {
                return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.None, Request: pending);
            }

            return new WorkingHoursRequestWorkflowResult(
                VacationRequestWorkflowFailure.Conflict,
                "У вас уже есть ожидающая заявка на изменение рабочих дней.");
        }

        var currentDays = await db.UserWorkingHoursDays.AsNoTracking()
            .Where(day => day.UserId == requester.Id)
            .OrderBy(day => day.DayOfWeek)
            .Select(day => new UserWorkingHoursDaySnapshot(
                day.DayOfWeek,
                day.IsWorkingDay,
                day.StartMinuteOfDay,
                day.EndMinuteOfDay))
            .ToListAsync(ct);
        if (currentDays.Count == 0)
        {
            currentDays = UserAvailabilityService.GetDefaultWorkingHours();
        }
        if (currentDays.OrderBy(day => day.DayOfWeek).Select(ToComparable).SequenceEqual(requestedDays.Select(ToComparable)))
        {
            return new WorkingHoursRequestWorkflowResult(
                VacationRequestWorkflowFailure.Conflict,
                "Новый график не отличается от текущего.");
        }

        var request = new WorkingHoursRequest
        {
            Id = Ulid.NewUlid(),
            RequesterUserId = requester.Id,
            SubjectUserId = requester.Id,
            Status = VacationRequestStatus.Pending,
            RequestMessage = NormalizeMessage(input.Message),
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Version = 1
        };
        request.RequestedWorkingHours = requestedDays.Select(day => new WorkingHoursRequestDay
        {
            Id = Ulid.NewUlid(),
            WorkingHoursRequestId = request.Id,
            WorkingHoursRequest = request,
            DayOfWeek = day.DayOfWeek,
            IsWorkingDay = day.IsWorkingDay,
            StartMinuteOfDay = day.StartMinuteOfDay,
            EndMinuteOfDay = day.EndMinuteOfDay
        }).ToList();

        await db.WorkingHoursRequests.AddAsync(request, ct);
        await db.SaveChangesAsync(ct);
        await NotifySuperusersAsync(request, "working_hours_request.created", "Новая заявка на изменение рабочих дней", ct);
        await WriteAuditAsync(request, MelodyTrack.Core.Auditing.AuditCatalog.Events.WorkingHoursRequestCreated, ct);
        await transaction.CommitAsync(ct);
        return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.None, Request: request);
    }

    public async Task<WorkingHoursRequestWorkflowResult> ApproveAsync(
        Ulid requestId,
        int expectedVersion,
        string? decisionMessage,
        User superuser,
        CancellationToken ct)
    {
        if (!superuser.Role.RoleName.IsSuperuser())
        {
            return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.Forbidden);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await db.WorkingHoursRequests
            .AsNoTracking()
            .Include(item => item.RequestedWorkingHours)
            .SingleOrDefaultAsync(item => item.Id == requestId, ct);
        if (request is null)
        {
            return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.NotFound, "Заявка на изменение рабочих дней не найдена.");
        }
        if (request.Status != VacationRequestStatus.Pending || request.Version != expectedVersion)
        {
            return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Заявка уже была обработана или изменена.");
        }
        if (request.RequesterUserId != request.SubjectUserId)
        {
            return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Связь заявителя с графиком больше недействительна.");
        }

        await subjectLock.AcquireWorkingHoursAsync(request.SubjectUserId, ct);
        var user = await db.Users
            .Include(item => item.Role)
            .Include(item => item.WorkingHours)
            .SingleOrDefaultAsync(item => item.Id == request.SubjectUserId, ct);
        if (user is null || (user.Role.RoleName != UserRoles.User && user.Role.RoleName != UserRoles.Admin))
        {
            return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Пользователь больше не может изменять рабочие дни через заявку.");
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var message = NormalizeMessage(decisionMessage);
        var updated = await db.WorkingHoursRequests
            .Where(item => item.Id == request.Id && item.Status == VacationRequestStatus.Pending && item.Version == expectedVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, VacationRequestStatus.Approved)
                .SetProperty(item => item.ProcessedAtUtc, nowUtc)
                .SetProperty(item => item.ProcessedBySuperuserId, superuser.Id)
                .SetProperty(item => item.DecisionMessage, message)
                .SetProperty(item => item.Version, item => item.Version + 1), ct);
        if (updated != 1)
        {
            return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Заявка уже была обработана другим пользователем.");
        }

        db.UserWorkingHoursDays.RemoveRange(user.WorkingHours);
        user.WorkingHours = request.RequestedWorkingHours.Select(day => new UserWorkingHoursDay
        {
            Id = Ulid.NewUlid(),
            UserId = user.Id,
            User = user,
            DayOfWeek = day.DayOfWeek,
            IsWorkingDay = day.IsWorkingDay,
            StartMinuteOfDay = day.StartMinuteOfDay,
            EndMinuteOfDay = day.EndMinuteOfDay
        }).ToList();

        await db.SaveChangesAsync(ct);
        await NotifyRequesterAsync(request, "working_hours_request.approved", "Рабочие дни согласованы", "Новый график уже действует.", ct);
        await WriteAuditAsync(request, MelodyTrack.Core.Auditing.AuditCatalog.Events.WorkingHoursRequestApproved, ct);
        await transaction.CommitAsync(ct);
        return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.None);
    }

    public async Task<WorkingHoursRequestWorkflowResult> DeclineAsync(
        Ulid requestId,
        int expectedVersion,
        string? decisionMessage,
        User superuser,
        CancellationToken ct)
    {
        if (!superuser.Role.RoleName.IsSuperuser())
        {
            return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.Forbidden);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await db.WorkingHoursRequests
            .AsNoTracking()
            .Include(item => item.RequestedWorkingHours)
            .SingleOrDefaultAsync(item => item.Id == requestId, ct);
        if (request is null)
        {
            return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.NotFound, "Заявка на изменение рабочих дней не найдена.");
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var message = NormalizeMessage(decisionMessage);
        var updated = await db.WorkingHoursRequests
            .Where(item => item.Id == request.Id && item.Status == VacationRequestStatus.Pending && item.Version == expectedVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, VacationRequestStatus.Declined)
                .SetProperty(item => item.ProcessedAtUtc, nowUtc)
                .SetProperty(item => item.ProcessedBySuperuserId, superuser.Id)
                .SetProperty(item => item.DecisionMessage, message)
                .SetProperty(item => item.Version, item => item.Version + 1), ct);
        if (updated != 1)
        {
            return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Заявка уже была обработана или изменена.");
        }

        await NotifyRequesterAsync(request, "working_hours_request.declined", "Изменение рабочих дней отклонено", "Решение доступно в приложении.", ct);
        await WriteAuditAsync(request, MelodyTrack.Core.Auditing.AuditCatalog.Events.WorkingHoursRequestDeclined, ct);
        await transaction.CommitAsync(ct);
        return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.None);
    }

    public async Task<WorkingHoursRequestWorkflowResult> CancelAsync(
        Ulid requestId,
        int expectedVersion,
        User requester,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await db.WorkingHoursRequests
            .AsNoTracking()
            .Include(item => item.RequestedWorkingHours)
            .SingleOrDefaultAsync(item => item.Id == requestId, ct);
        if (request is null)
        {
            return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.NotFound, "Заявка на изменение рабочих дней не найдена.");
        }
        if (request.RequesterUserId != requester.Id || requester.Role.RoleName.IsClient())
        {
            return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.Forbidden);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var updated = await db.WorkingHoursRequests
            .Where(item => item.Id == request.Id && item.Status == VacationRequestStatus.Pending && item.Version == expectedVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, VacationRequestStatus.Cancelled)
                .SetProperty(item => item.ProcessedAtUtc, nowUtc)
                .SetProperty(item => item.Version, item => item.Version + 1), ct);
        if (updated != 1)
        {
            return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Отменить можно только свою ожидающую заявку.");
        }

        await NotifySuperusersAsync(request, "working_hours_request.cancelled", "Заявка на изменение рабочих дней отозвана", ct);
        await WriteAuditAsync(request, MelodyTrack.Core.Auditing.AuditCatalog.Events.WorkingHoursRequestCancelled, ct);
        await transaction.CommitAsync(ct);
        return new WorkingHoursRequestWorkflowResult(VacationRequestWorkflowFailure.None);
    }

    private static List<WorkingHoursRequestDay> MapInput(IReadOnlyCollection<WorkingHoursRequestDayInput> input) =>
        input.Select(day =>
        {
            var start = day.IsWorkingDay && TimeOnly.TryParse(day.StartTime, out var startTime) ? startTime : new TimeOnly(10, 0);
            var end = day.IsWorkingDay && TimeOnly.TryParse(day.EndTime, out var endTime) ? endTime : new TimeOnly(20, 0);
            return new WorkingHoursRequestDay
            {
                Id = Ulid.NewUlid(),
                WorkingHoursRequestId = Ulid.Empty,
                WorkingHoursRequest = null!,
                DayOfWeek = ParseDayOfWeek(day.DayOfWeek),
                IsWorkingDay = day.IsWorkingDay,
                StartMinuteOfDay = start.Hour * 60 + start.Minute,
                EndMinuteOfDay = end.Hour * 60 + end.Minute
            };
        }).OrderBy(day => day.DayOfWeek).ToList();

    private static bool SchedulesEqual(IReadOnlyCollection<WorkingHoursRequestDay> left, IReadOnlyCollection<WorkingHoursRequestDay> right) =>
        left.OrderBy(day => day.DayOfWeek).Select(ToComparable).SequenceEqual(right.OrderBy(day => day.DayOfWeek).Select(ToComparable));

    private static ScheduleDay ToComparable(WorkingHoursRequestDay day) =>
        new(day.DayOfWeek, day.IsWorkingDay, day.StartMinuteOfDay, day.EndMinuteOfDay);

    private static ScheduleDay ToComparable(UserWorkingHoursDaySnapshot day) =>
        new(day.DayOfWeek, day.IsWorkingDay, day.StartMinuteOfDay, day.EndMinuteOfDay);

    private async Task NotifySuperusersAsync(WorkingHoursRequest request, string type, string title, CancellationToken ct)
    {
        var superuserIds = await db.Users.AsNoTracking()
            .Where(user => user.Role.RoleName == UserRoles.Superuser)
            .Select(user => user.Id)
            .ToListAsync(ct);
        foreach (var superuserId in superuserIds)
        {
            await notificationService.CreateAsync(new NotificationDraft(
                superuserId, null, type, title,
                "Откройте очередь заявок, чтобы сравнить текущий и запрошенный график.",
                "В приложении обновилась заявка на изменение рабочих дней.",
                "/vacation-requests", "working_hours_request", request.Id), ct);
        }
    }

    private async Task NotifyRequesterAsync(WorkingHoursRequest request, string type, string title, string summary, CancellationToken ct)
    {
        await notificationService.CreateAsync(new NotificationDraft(
            request.RequesterUserId, null, type, title, summary,
            "В приложении обновился статус заявки на изменение рабочих дней.",
            "/vacation-requests", "working_hours_request", request.Id), ct);
    }

    private Task WriteAuditAsync(
        WorkingHoursRequest request,
        MelodyTrack.Core.Auditing.AuditEventDefinition auditEvent,
        CancellationToken ct) =>
        auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = auditEvent,
            EntityType = "working_hours_request",
            EntityId = request.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Пользователь", request.SubjectUserId.ToString()),
                AuditDetailsFormatter.DescribeContext("Рабочих дней", request.RequestedWorkingHours.Count(day => day.IsWorkingDay).ToString()))
        }, ct);

    private static DayOfWeek ParseDayOfWeek(string value) => value.Trim().ToLowerInvariant() switch
    {
        "monday" => DayOfWeek.Monday,
        "tuesday" => DayOfWeek.Tuesday,
        "wednesday" => DayOfWeek.Wednesday,
        "thursday" => DayOfWeek.Thursday,
        "friday" => DayOfWeek.Friday,
        "saturday" => DayOfWeek.Saturday,
        _ => DayOfWeek.Sunday
    };

    private static string? NormalizeMessage(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private readonly record struct ScheduleDay(
        DayOfWeek DayOfWeek,
        bool IsWorkingDay,
        int StartMinuteOfDay,
        int EndMinuteOfDay);
}
