using MelodyTrack.Backend.Api.VacationRequests.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Notifications;
using MelodyTrack.Backend.Utils;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Services;

public enum VacationRequestWorkflowFailure
{
    None,
    NotFound,
    Forbidden,
    Conflict
}

public sealed record VacationRequestWorkflowResult(
    VacationRequestWorkflowFailure Failure,
    string? Detail = null,
    VacationRequest? Request = null)
{
    public static VacationRequestWorkflowResult Success(VacationRequest request) =>
        new(VacationRequestWorkflowFailure.None, Request: request);
}

public interface IVacationRequestWorkflowService
{
    Task<VacationRequestWorkflowResult> CreateStaffRequestAsync(
        User requester,
        CreateVacationRequest request,
        CancellationToken ct);

    Task<VacationRequestWorkflowResult> CreateClientRequestAsync(
        User portalUser,
        CreateVacationRequest request,
        CancellationToken ct);

    Task<VacationRequestWorkflowResult> ApproveAsync(
        Ulid requestId,
        int expectedVersion,
        string? decisionMessage,
        bool cancelConflictingAppointments,
        User superuser,
        CancellationToken ct);

    Task<VacationRequestWorkflowResult> DeclineAsync(
        Ulid requestId,
        int expectedVersion,
        string? decisionMessage,
        User superuser,
        CancellationToken ct);

    Task<VacationRequestWorkflowResult> CancelAsync(
        Ulid requestId,
        int expectedVersion,
        User requester,
        CancellationToken ct);
}

public sealed class VacationRequestWorkflowService(
    AppDbContext db,
    INotificationService notificationService,
    IAuditLogService auditLogService,
    IVacationRequestSubjectLock subjectLock,
    TimeProvider timeProvider) : IVacationRequestWorkflowService
{
    public Task<VacationRequestWorkflowResult> CreateStaffRequestAsync(
        User requester,
        CreateVacationRequest request,
        CancellationToken ct)
    {
        var role = requester.Role.RoleName;
        if (role.IsClient() || role.IsSuperuser() || (role & (UserRoles.User | UserRoles.Admin)) == 0)
        {
            return Task.FromResult(new VacationRequestWorkflowResult(
                VacationRequestWorkflowFailure.Forbidden,
                "Заявки на собственный отпуск доступны преподавателям и администраторам."));
        }

        return CreateAsync(
            VacationRequestPrincipalType.Staff,
            requester.Id,
            VacationRequestSubjectType.Staff,
            requester.Id,
            request,
            ct);
    }

    public Task<VacationRequestWorkflowResult> CreateClientRequestAsync(
        User portalUser,
        CreateVacationRequest request,
        CancellationToken ct)
    {
        if (!portalUser.Role.RoleName.IsClient() || portalUser.ClientId is null)
        {
            return Task.FromResult(new VacationRequestWorkflowResult(
                VacationRequestWorkflowFailure.Forbidden,
                "Заявка должна быть создана из действующего кабинета клиента."));
        }

        return CreateAsync(
            VacationRequestPrincipalType.Client,
            portalUser.ClientId.Value,
            VacationRequestSubjectType.Client,
            portalUser.ClientId.Value,
            request,
            ct);
    }

    public async Task<VacationRequestWorkflowResult> ApproveAsync(
        Ulid requestId,
        int expectedVersion,
        string? decisionMessage,
        bool cancelConflictingAppointments,
        User superuser,
        CancellationToken ct)
    {
        if (!superuser.Role.RoleName.IsSuperuser())
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.Forbidden);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await db.VacationRequests
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == requestId, ct);
        if (request is null)
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.NotFound, "Заявка на отпуск не найдена.");
        }
        if (request.Status != VacationRequestStatus.Pending || request.Version != expectedVersion)
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Заявка уже была обработана или изменена.");
        }
        if (!await SubjectAndRequesterRemainValidAsync(request, ct))
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Связь заявителя с получателем отпуска больше недействительна.");
        }

        await subjectLock.AcquireAsync(request.SubjectType, request.SubjectId, ct);
        if (await HasExistingVacationAsync(request.SubjectType, request.SubjectId, request.RequestedStart, request.RequestedEnd, ct))
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Запрошенный период пересекается с существующим отпуском.");
        }
        var conflictingAppointments = await GetConflictingAppointmentsAsync(
            request.SubjectType,
            request.SubjectId,
            request.RequestedStart,
            request.RequestedEnd,
            ct);
        if (conflictingAppointments.Count > 0 && !cancelConflictingAppointments)
        {
            return new VacationRequestWorkflowResult(
                VacationRequestWorkflowFailure.Conflict,
                $"В запрошенном периоде есть запланированные занятия: {conflictingAppointments.Count}. Разрешите их отмену при одобрении или сначала измените расписание; занятия не были изменены.");
        }

        foreach (var appointment in conflictingAppointments)
        {
            appointment.Status = AppointmentStatus.Cancelled;
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var vacationId = Ulid.NewUlid();
        var updated = await db.VacationRequests
            .Where(item => item.Id == request.Id && item.Status == VacationRequestStatus.Pending && item.Version == expectedVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, VacationRequestStatus.Approved)
                .SetProperty(item => item.ProcessedAtUtc, nowUtc)
                .SetProperty(item => item.ProcessedBySuperuserId, superuser.Id)
                .SetProperty(item => item.DecisionMessage, NormalizeMessage(decisionMessage))
                .SetProperty(item => item.ResultingVacationId, vacationId)
                .SetProperty(item => item.Version, item => item.Version + 1), ct);
        if (updated != 1)
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Заявка уже была обработана другим пользователем.");
        }

        if (request.SubjectType == VacationRequestSubjectType.Staff)
        {
            var user = await db.Users.SingleAsync(item => item.Id == request.SubjectId, ct);
            await db.UserVacations.AddAsync(new UserVacation
            {
                Id = vacationId,
                UserId = user.Id,
                User = user,
                StartDate = request.RequestedStart,
                EndDate = request.RequestedEnd
            }, ct);
        }
        else
        {
            var client = await db.Clients.SingleAsync(item => item.Id == request.SubjectId, ct);
            await db.ClientVacations.AddAsync(new ClientVacation
            {
                Id = vacationId,
                ClientId = client.Id,
                Client = client,
                StartDate = request.RequestedStart,
                EndDate = request.RequestedEnd
            }, ct);
        }

        await db.SaveChangesAsync(ct);
        await WriteCancelledAppointmentAuditsAsync(conflictingAppointments, ct);
        var cancelledSummary = conflictingAppointments.Count > 0
            ? $" Пересекающиеся занятия отменены: {conflictingAppointments.Count}."
            : string.Empty;
        await NotifyRequesterAsync(
            request,
            "vacation_request.approved",
            "Отпуск согласован",
            $"Заявка одобрена. Отпуск добавлен в календарь.{cancelledSummary}",
            ct);
        await WriteAuditAsync(
            request,
            MelodyTrack.Core.Auditing.AuditCatalog.Events.VacationRequestApproved,
            ct,
            conflictingAppointments.Count);
        await transaction.CommitAsync(ct);

        return VacationRequestWorkflowResult.Success(request.WithState(
            VacationRequestStatus.Approved,
            expectedVersion + 1,
            nowUtc,
            superuser.Id,
            NormalizeMessage(decisionMessage),
            vacationId));
    }

    public async Task<VacationRequestWorkflowResult> DeclineAsync(
        Ulid requestId,
        int expectedVersion,
        string? decisionMessage,
        User superuser,
        CancellationToken ct)
    {
        if (!superuser.Role.RoleName.IsSuperuser())
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.Forbidden);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await db.VacationRequests.AsNoTracking().SingleOrDefaultAsync(item => item.Id == requestId, ct);
        if (request is null)
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.NotFound, "Заявка на отпуск не найдена.");
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var message = NormalizeMessage(decisionMessage);
        var updated = await db.VacationRequests
            .Where(item => item.Id == request.Id && item.Status == VacationRequestStatus.Pending && item.Version == expectedVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, VacationRequestStatus.Declined)
                .SetProperty(item => item.ProcessedAtUtc, nowUtc)
                .SetProperty(item => item.ProcessedBySuperuserId, superuser.Id)
                .SetProperty(item => item.DecisionMessage, message)
                .SetProperty(item => item.Version, item => item.Version + 1), ct);
        if (updated != 1)
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Заявка уже была обработана или изменена.");
        }

        await NotifyRequesterAsync(request, "vacation_request.declined", "Заявка на отпуск отклонена", "Решение доступно в приложении.", ct);
        await WriteAuditAsync(request, MelodyTrack.Core.Auditing.AuditCatalog.Events.VacationRequestDeclined, ct);
        await transaction.CommitAsync(ct);

        return VacationRequestWorkflowResult.Success(request.WithState(
            VacationRequestStatus.Declined,
            expectedVersion + 1,
            nowUtc,
            superuser.Id,
            message));
    }

    public async Task<VacationRequestWorkflowResult> CancelAsync(
        Ulid requestId,
        int expectedVersion,
        User requester,
        CancellationToken ct)
    {
        var principalType = requester.Role.RoleName.IsClient()
            ? VacationRequestPrincipalType.Client
            : VacationRequestPrincipalType.Staff;
        var principalId = principalType == VacationRequestPrincipalType.Client ? requester.ClientId : requester.Id;
        if (principalId is null)
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.Forbidden);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var request = await db.VacationRequests.AsNoTracking().SingleOrDefaultAsync(item => item.Id == requestId, ct);
        if (request is null)
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.NotFound, "Заявка на отпуск не найдена.");
        }
        if (request.RequesterPrincipalType != principalType || request.RequesterId != principalId.Value)
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.Forbidden);
        }

        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var updated = await db.VacationRequests
            .Where(item => item.Id == request.Id && item.Status == VacationRequestStatus.Pending && item.Version == expectedVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(item => item.Status, VacationRequestStatus.Cancelled)
                .SetProperty(item => item.ProcessedAtUtc, nowUtc)
                .SetProperty(item => item.Version, item => item.Version + 1), ct);
        if (updated != 1)
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Отменить можно только свою ожидающую заявку.");
        }

        await NotifySuperusersAsync(request, "vacation_request.cancelled", "Заявка на отпуск отозвана", ct);
        await WriteAuditAsync(request, MelodyTrack.Core.Auditing.AuditCatalog.Events.VacationRequestCancelled, ct);
        await transaction.CommitAsync(ct);

        return VacationRequestWorkflowResult.Success(request.WithState(VacationRequestStatus.Cancelled, expectedVersion + 1, nowUtc));
    }

    private async Task<VacationRequestWorkflowResult> CreateAsync(
        VacationRequestPrincipalType requesterType,
        Ulid requesterId,
        VacationRequestSubjectType subjectType,
        Ulid subjectId,
        CreateVacationRequest input,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await subjectLock.AcquireAsync(subjectType, subjectId, ct);

        if (await HasExistingVacationAsync(subjectType, subjectId, input.StartDate, input.EndDate, ct))
        {
            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Запрошенный период пересекается с существующим отпуском.");
        }
        var overlappingPendingRequest = await db.VacationRequests.AsNoTracking().FirstOrDefaultAsync(item =>
                item.SubjectType == subjectType &&
                item.SubjectId == subjectId &&
                item.Status == VacationRequestStatus.Pending &&
                item.RequestedStart < input.EndDate &&
                item.RequestedEnd > input.StartDate, ct);
        if (overlappingPendingRequest is not null)
        {
            if (overlappingPendingRequest.RequesterPrincipalType == requesterType &&
                overlappingPendingRequest.RequesterId == requesterId &&
                overlappingPendingRequest.RequestedStart == input.StartDate &&
                overlappingPendingRequest.RequestedEnd == input.EndDate &&
                overlappingPendingRequest.RequestMessage == NormalizeMessage(input.Message))
            {
                return VacationRequestWorkflowResult.Success(overlappingPendingRequest);
            }

            return new VacationRequestWorkflowResult(VacationRequestWorkflowFailure.Conflict, "Для этого периода уже есть ожидающая заявка.");
        }

        var request = new VacationRequest
        {
            Id = Ulid.NewUlid(),
            RequesterPrincipalType = requesterType,
            RequesterId = requesterId,
            SubjectType = subjectType,
            SubjectId = subjectId,
            RequestedStart = input.StartDate,
            RequestedEnd = input.EndDate,
            Status = VacationRequestStatus.Pending,
            RequestMessage = NormalizeMessage(input.Message),
            CreatedAtUtc = timeProvider.GetUtcNow().UtcDateTime,
            Version = 1
        };

        await db.VacationRequests.AddAsync(request, ct);
        await db.SaveChangesAsync(ct);
        await NotifySuperusersAsync(request, "vacation_request.created", "Новая заявка на отпуск", ct);
        await WriteAuditAsync(request, MelodyTrack.Core.Auditing.AuditCatalog.Events.VacationRequestCreated, ct);
        await transaction.CommitAsync(ct);
        return VacationRequestWorkflowResult.Success(request);
    }

    private async Task<bool> SubjectAndRequesterRemainValidAsync(VacationRequest request, CancellationToken ct)
    {
        if (request.RequesterPrincipalType == VacationRequestPrincipalType.Staff &&
            request.SubjectType == VacationRequestSubjectType.Staff &&
            request.RequesterId == request.SubjectId)
        {
            return await db.Users.AsNoTracking().AnyAsync(user =>
                user.Id == request.SubjectId &&
                (user.Role.RoleName == UserRoles.User || user.Role.RoleName == UserRoles.Admin), ct);
        }

        if (request.RequesterPrincipalType == VacationRequestPrincipalType.Client &&
            request.SubjectType == VacationRequestSubjectType.Client &&
            request.RequesterId == request.SubjectId)
        {
            return await db.Clients.AsNoTracking().AnyAsync(client => client.Id == request.SubjectId, ct);
        }

        return false;
    }

    private Task<bool> HasExistingVacationAsync(
        VacationRequestSubjectType subjectType,
        Ulid subjectId,
        DateTime start,
        DateTime end,
        CancellationToken ct)
    {
        return subjectType == VacationRequestSubjectType.Staff
            ? db.UserVacations.AsNoTracking().AnyAsync(item =>
                item.UserId == subjectId && item.StartDate < end && item.EndDate > start, ct)
            : db.ClientVacations.AsNoTracking().AnyAsync(item =>
                item.ClientId == subjectId && item.StartDate < end && item.EndDate > start, ct);
    }

    private Task<List<Appointment>> GetConflictingAppointmentsAsync(
        VacationRequestSubjectType subjectType,
        Ulid subjectId,
        DateTime start,
        DateTime end,
        CancellationToken ct)
    {
        var appointments = db.Appointments
            .Include(item => item.Client)
            .Include(item => item.Service)
            .Include(item => item.Provider)
            .Where(item =>
                !item.IsDeleted &&
                item.Status == AppointmentStatus.Planned &&
                item.StartDate < end &&
                item.EndDate > start);

        return subjectType == VacationRequestSubjectType.Staff
            ? appointments.Where(item => item.Provider != null && item.Provider.Id == subjectId).ToListAsync(ct)
            : appointments.Where(item => item.Client.Id == subjectId).ToListAsync(ct);
    }

    private async Task WriteCancelledAppointmentAuditsAsync(
        IReadOnlyCollection<Appointment> appointments,
        CancellationToken ct)
    {
        foreach (var appointment in appointments)
        {
            await auditLogService.WriteAsync(new AuditLogWriteRequest
            {
                Event = MelodyTrack.Core.Auditing.AuditCatalog.Events.AppointmentUpdated,
                EntityType = "appointment",
                EntityId = appointment.Id.ToString(),
                Details = AuditDetailsFormatter.JoinChanges(
                    AuditDetailsFormatter.DescribeContext("Клиент", $"{appointment.Client.LastName} {appointment.Client.FirstName}".Trim()),
                    AuditDetailsFormatter.DescribeContext("Услуга", appointment.Service.Name),
                    AuditDetailsFormatter.DescribeContext("Начало", appointment.StartDate),
                    AuditDetailsFormatter.DescribeChange("Статус", AppointmentStatus.Planned.ToDisplayName(), AppointmentStatus.Cancelled.ToDisplayName()),
                    AuditDetailsFormatter.DescribeContext("Причина", "Отпуск"))
            }, ct);
        }
    }

    private async Task NotifySuperusersAsync(
        VacationRequest request,
        string type,
        string title,
        CancellationToken ct)
    {
        var superuserIds = await db.Users.AsNoTracking()
            .Where(user => user.Role.RoleName == UserRoles.Superuser)
            .Select(user => user.Id)
            .ToListAsync(ct);
        foreach (var superuserId in superuserIds)
        {
            await notificationService.CreateAsync(new NotificationDraft(
                superuserId,
                null,
                type,
                title,
                "Откройте очередь заявок, чтобы посмотреть период и статус.",
                "В приложении обновилась заявка на отпуск.",
                "/vacation-requests",
                "vacation_request",
                request.Id), ct);
        }
    }

    private async Task NotifyRequesterAsync(
        VacationRequest request,
        string type,
        string title,
        string summary,
        CancellationToken ct)
    {
        await notificationService.CreateAsync(new NotificationDraft(
            request.RequesterPrincipalType == VacationRequestPrincipalType.Staff ? request.RequesterId : null,
            request.RequesterPrincipalType == VacationRequestPrincipalType.Client ? request.RequesterId : null,
            type,
            title,
            summary,
            "В приложении обновился статус заявки на отпуск.",
            request.RequesterPrincipalType == VacationRequestPrincipalType.Staff ? "/vacation-requests" : "/portal/vacations",
            "vacation_request",
            request.Id), ct);
    }

    private Task WriteAuditAsync(
        VacationRequest request,
        MelodyTrack.Core.Auditing.AuditEventDefinition auditEvent,
        CancellationToken ct,
        int cancelledAppointmentCount = 0)
    {
        return auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = auditEvent,
            EntityType = "vacation_request",
            EntityId = request.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Тип получателя", request.SubjectType.ToString()),
                AuditDetailsFormatter.DescribeContext("Получатель", request.SubjectId.ToString()),
                AuditDetailsFormatter.DescribeContext("Период", $"{request.RequestedStart:yyyy-MM-dd}–{request.RequestedEnd:yyyy-MM-dd}"),
                cancelledAppointmentCount > 0
                    ? AuditDetailsFormatter.DescribeContext("Отменено занятий", cancelledAppointmentCount.ToString())
                    : null)
        }, ct);
    }

    private static string? NormalizeMessage(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

file static class VacationRequestStateExtensions
{
    public static VacationRequest WithState(
        this VacationRequest request,
        VacationRequestStatus status,
        int version,
        DateTime processedAtUtc,
        Ulid? processedBySuperuserId = null,
        string? decisionMessage = null,
        Ulid? resultingVacationId = null)
    {
        request.Status = status;
        request.Version = version;
        request.ProcessedAtUtc = processedAtUtc;
        request.ProcessedBySuperuserId = processedBySuperuserId;
        request.DecisionMessage = decisionMessage;
        request.ResultingVacationId = resultingVacationId;
        return request;
    }
}
