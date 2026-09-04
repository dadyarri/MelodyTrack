using MelodyTrack.Backend.ErrorHandling;
using MelodyTrack.Backend.Api;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Users.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Users.Endpoints;

[ApiEndpoint(ApiMethod.Put, "/users/{id}/availability")]
public sealed class UpdateUserAvailabilityEndpoint
{

    public static async Task<Results<NoContent, UnauthorizedHttpResult, ForbidHttpResult, NotFound<ApiProblemDetails>, Conflict<StaleEntityConflictResponse>, Conflict<ApiProblemDetails>>> HandleAsync(
        UpdateUserAvailabilityRequest req,
        Ulid id,
        AppDbContext db,
        IEntityFreshnessService entityFreshnessService,
        IAuditLogService auditLogService,
        IVacationRequestSubjectLock vacationRequestSubjectLock,
        ICurrentUserAccessor currentUserAccessor,
        HttpContext httpContext,
        ApiValidationErrorCollection validationErrors,
        CancellationToken ct
    )
    {
        req.Id = id;
        var currentUser = await currentUserAccessor.GetAsync(ct);
        if (currentUser is null)
        {
            return TypedResults.Unauthorized();
        }

        if (currentUser.Id != req.Id && !currentUser.Role.RoleName.IsAnyAdmin())
        {
            return TypedResults.Forbid();
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await vacationRequestSubjectLock.AcquireAsync(VacationRequestSubjectType.Staff, req.Id, ct);
        await vacationRequestSubjectLock.AcquireWorkingHoursAsync(req.Id, ct);

        var user = await db.Users
            .Include(e => e.Role)
            .Include(e => e.WorkingHours)
            .Include(e => e.Vacations)
            .FirstOrDefaultAsync(e => e.Id == req.Id, ct);

        if (user is null)
        {
            validationErrors.Add(nameof(req.Id), "Пользователь не найден");
            return TypedResults.NotFound(new ApiProblemDetails(validationErrors, httpContext, StatusCodes.Status404NotFound));
        }

        if (user.Role.RoleName.IsSuperuser() && !currentUser.Role.RoleName.IsSuperuser())
        {
            return TypedResults.Forbid();
        }

        var vacationsChanged = AreVacationsChanged(user, req);
        var workingHoursChanged = AreWorkingHoursChanged(user, req);
        if ((vacationsChanged || workingHoursChanged) && !currentUser.Role.RoleName.IsSuperuser())
        {
            return TypedResults.Forbid();
        }

        var conflict = await entityFreshnessService.GetConflictIfStaleAsync(
            "user_availability",
            user.Id,
            req.ExpectedActivityId,
            "График работы был изменен другим пользователем. Обновите данные и повторите сохранение.",
            ct);

        if (conflict is not null && !IsNoOp(user, req))
        {
            return TypedResults.Conflict(conflict);
        }

        if (!vacationsChanged && !workingHoursChanged)
        {
            await transaction.CommitAsync(ct);
            return TypedResults.NoContent();
        }

        List<Appointment> conflictingAppointments = vacationsChanged
            ? await GetConflictingAppointmentsAsync(db, user.Id, req.Vacations, ct)
            : [];
        if (conflictingAppointments.Count > 0 && !req.CancelConflictingAppointments)
        {
            validationErrors.Add(
                nameof(req.Vacations),
                $"Выбранные отпуска пересекаются с запланированными занятиями: {conflictingAppointments.Count}. Разрешите их отмену или сначала измените расписание.",
                "appointment_conflict");
            ApiProblemDetails problem = new(validationErrors, httpContext, StatusCodes.Status409Conflict);
            return TypedResults.Conflict(problem);
        }

        foreach (var appointment in conflictingAppointments)
        {
            appointment.Status = AppointmentStatus.Cancelled;
        }

        db.UserWorkingHoursDays.RemoveRange(user.WorkingHours);

        user.WorkingHours = req.WorkingHours
            .Select(item => new UserWorkingHoursDay
            {
                Id = Ulid.NewUlid(),
                UserId = user.Id,
                User = user,
                DayOfWeek = ParseDayOfWeek(item.DayOfWeek),
                IsWorkingDay = item.IsWorkingDay,
                StartMinuteOfDay = item.IsWorkingDay && TimeOnly.TryParse(item.StartTime, out var startTime)
                    ? startTime.Hour * 60 + startTime.Minute
                    : 10 * 60,
                EndMinuteOfDay = item.IsWorkingDay && TimeOnly.TryParse(item.EndTime, out var endTime)
                    ? endTime.Hour * 60 + endTime.Minute
                    : 20 * 60
            })
            .ToList();

        if (vacationsChanged)
        {
            db.UserVacations.RemoveRange(user.Vacations);
            user.Vacations = req.Vacations
                .Select(item => new UserVacation
                {
                    Id = Ulid.NewUlid(),
                    UserId = user.Id,
                    User = user,
                    StartDate = item.StartDate,
                    EndDate = item.EndDate
                })
                .ToList();
        }

        await db.SaveChangesAsync(ct);
        await WriteCancelledAppointmentAuditsAsync(auditLogService, conflictingAppointments, ct);
        if (workingHoursChanged)
        {
            await WriteDirectAuditAsync(
                auditLogService,
                user,
                MelodyTrack.Core.Auditing.AuditCatalog.Events.UserWorkingHoursUpdatedDirectly,
                ct);
        }
        if (vacationsChanged)
        {
            await WriteDirectAuditAsync(
                auditLogService,
                user,
                MelodyTrack.Core.Auditing.AuditCatalog.Events.UserVacationsUpdatedDirectly,
                ct);
        }
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static async Task<List<Appointment>> GetConflictingAppointmentsAsync(
        AppDbContext db,
        Ulid userId,
        IReadOnlyCollection<UserVacationItem> vacations,
        CancellationToken ct)
    {
        if (vacations.Count == 0)
        {
            return [];
        }

        var earliestStart = vacations.Min(item => item.StartDate);
        var latestEnd = vacations.Max(item => item.EndDate);
        var candidates = await db.Appointments
            .Include(item => item.Client)
            .Include(item => item.Service)
            .Where(item =>
                !item.IsDeleted &&
                item.Status == AppointmentStatus.Planned &&
                item.Provider != null &&
                item.Provider.Id == userId &&
                item.StartDate < latestEnd &&
                item.EndDate > earliestStart)
            .ToListAsync(ct);

        return candidates
            .Where(item => vacations.Any(vacation => item.StartDate < vacation.EndDate && item.EndDate > vacation.StartDate))
            .ToList();
    }

    private static async Task WriteCancelledAppointmentAuditsAsync(
        IAuditLogService auditLogService,
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

    private static bool IsNoOp(User user, UpdateUserAvailabilityRequest req)
    {
        var currentWorkingHours = user.WorkingHours
            .OrderBy(item => item.DayOfWeek)
            .Select(item => new
            {
                DayOfWeek = MapDayOfWeek(item.DayOfWeek),
                item.IsWorkingDay,
                StartTime = item.IsWorkingDay ? FormatTime(item.StartMinuteOfDay) : null,
                EndTime = item.IsWorkingDay ? FormatTime(item.EndMinuteOfDay) : null
            })
            .ToList();

        var requestedWorkingHours = req.WorkingHours
            .OrderBy(item => item.DayOfWeek)
            .Select(item => new
            {
                DayOfWeek = item.DayOfWeek.Trim().ToLowerInvariant(),
                item.IsWorkingDay,
                StartTime = item.IsWorkingDay ? item.StartTime : null,
                EndTime = item.IsWorkingDay ? item.EndTime : null
            })
            .ToList();

        var currentVacations = user.Vacations
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.EndDate)
            .Select(item => new { item.StartDate, item.EndDate })
            .ToList();

        var requestedVacations = req.Vacations
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.EndDate)
            .Select(item => new { item.StartDate, item.EndDate })
            .ToList();

        return currentWorkingHours.SequenceEqual(requestedWorkingHours) && currentVacations.SequenceEqual(requestedVacations);
    }

    private static bool AreVacationsChanged(User user, UpdateUserAvailabilityRequest req)
    {
        var current = user.Vacations
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.EndDate)
            .Select(item => (item.StartDate, item.EndDate));
        var requested = req.Vacations
            .OrderBy(item => item.StartDate)
            .ThenBy(item => item.EndDate)
            .Select(item => (item.StartDate, item.EndDate));

        return !current.SequenceEqual(requested);
    }

    private static bool AreWorkingHoursChanged(User user, UpdateUserAvailabilityRequest req)
    {
        var current = user.WorkingHours
            .OrderBy(item => item.DayOfWeek)
            .Select(item => (MapDayOfWeek(item.DayOfWeek), item.IsWorkingDay,
                item.IsWorkingDay ? FormatTime(item.StartMinuteOfDay) : null,
                item.IsWorkingDay ? FormatTime(item.EndMinuteOfDay) : null));
        var requested = req.WorkingHours
            .OrderBy(item => item.DayOfWeek)
            .Select(item => (item.DayOfWeek.Trim().ToLowerInvariant(), item.IsWorkingDay,
                item.IsWorkingDay ? item.StartTime : null,
                item.IsWorkingDay ? item.EndTime : null));

        return !current.SequenceEqual(requested);
    }

    private static Task WriteDirectAuditAsync(
        IAuditLogService auditLogService,
        User user,
        MelodyTrack.Core.Auditing.AuditEventDefinition auditEvent,
        CancellationToken ct)
    {
        return auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Event = auditEvent,
            EntityType = "user_availability",
            EntityId = user.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Пользователь", $"{user.LastName} {user.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeContext("Рабочих дней", user.WorkingHours.Count(item => item.IsWorkingDay).ToString()),
                AuditDetailsFormatter.DescribeContext("Отпусков", user.Vacations.Count.ToString()))
        }, ct);
    }

    private static DayOfWeek ParseDayOfWeek(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "monday" => DayOfWeek.Monday,
            "tuesday" => DayOfWeek.Tuesday,
            "wednesday" => DayOfWeek.Wednesday,
            "thursday" => DayOfWeek.Thursday,
            "friday" => DayOfWeek.Friday,
            "saturday" => DayOfWeek.Saturday,
            _ => DayOfWeek.Sunday
        };
    }

    private static string MapDayOfWeek(DayOfWeek value)
    {
        return value switch
        {
            DayOfWeek.Monday => "monday",
            DayOfWeek.Tuesday => "tuesday",
            DayOfWeek.Wednesday => "wednesday",
            DayOfWeek.Thursday => "thursday",
            DayOfWeek.Friday => "friday",
            DayOfWeek.Saturday => "saturday",
            _ => "sunday"
        };
    }

    private static string FormatTime(int totalMinutes)
    {
        return $"{totalMinutes / 60:00}:{totalMinutes % 60:00}";
    }
}
