using FastEndpoints;
using MelodyTrack.Backend.Api.Common.Responses;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using MelodyTrack.Backend.Utils;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule.Endpoints;

public class CreateAppointmentEndpoint(
    AppDbContext db,
    IAuditLogService auditLogService,
    IRequestReplayService requestReplayService,
    IUserAvailabilityService userAvailabilityService)
    : Ep.Req<CreateAppointmentRequest>.Res<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, NotFound<ProblemDetails>, ProblemDetails>>
{
    private const string ReplayEndpoint = "appointments:create";

    public override void Configure()
    {
        Post("/appointments");
    }

    public override async Task<Results<Created<CreateEntityResponse>, UnauthorizedHttpResult, NotFound<ProblemDetails>, ProblemDetails>> ExecuteAsync(CreateAppointmentRequest req, CancellationToken ct)
    {
        var replayKey = requestReplayService.GetReplayKey(HttpContext.Request.Headers);
        await using var transaction = replayKey is null ? null : await db.Database.BeginTransactionAsync(ct);
        Ulid? reservationId = null;
        if (replayKey is not null)
        {
            var decision = await requestReplayService.AcquireAsync(ReplayEndpoint, replayKey, req, ct);
            if (decision.Status == RequestReplayStatus.Completed)
            {
                return TypedResults.Created($"/appointments/{decision.ResponseEntityId}", new CreateEntityResponse
                {
                    Id = decision.ResponseEntityId!.Value
                });
            }

            reservationId = decision.ReservationId;
        }

        var client = await db.Clients.Where(e => e.Id == req.ClientId).FirstOrDefaultAsync(ct);

        if (client is null)
        {
            AddError(e => e.ClientId, "Клиент не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        var service = await db.Services.Where(e => e.Id == req.ServiceId).FirstOrDefaultAsync(ct);

        if (service is null)
        {
            AddError(e => e.ServiceId, "Сервис не найден");
            return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
        }

        var provider = await db.Users.Where(e => e.Id == req.ProviderId).FirstOrDefaultAsync(ct);

        if (provider is not null)
        {
            var isAvailable = await userAvailabilityService.IsAvailableAsync(
                provider.Id,
                req.StartDate.ToUniversalTime(),
                req.StartDate.AddHours(1).ToUniversalTime(),
                req.Timezone,
                ct);

            if (!isAvailable)
            {
                AddError(e => e.StartDate, "Запись попадает в нерабочее время преподавателя или в отпуск.");
                return new ProblemDetails(ValidationFailures);
            }
        }

        var recurrenceType = await db.RecurrenceTypes.Where(e => e.Id == req.RecurrenceTypeId).FirstOrDefaultAsync(ct);
        CourseTheme? courseTheme = null;

        if (req.CourseThemeId is not null)
        {
            courseTheme = await db.CourseThemes
                .Include(item => item.Branch)
                    .ThenInclude(item => item.Block)
                        .ThenInclude(item => item.Course)
                .FirstOrDefaultAsync(item => item.Id == req.CourseThemeId.Value, ct);

            if (courseTheme is null)
            {
                AddError(e => e.CourseThemeId, "Тема курса не найдена");
                return TypedResults.NotFound(new ProblemDetails(ValidationFailures));
            }

            var hasEnrollment = await db.CourseEnrollments
                .AsNoTracking()
                .AnyAsync(item => item.ClientId == client.Id && item.CourseId == courseTheme.Branch.Block.CourseId, ct);

            if (!hasEnrollment)
            {
                AddError(e => e.CourseThemeId, "Эта тема недоступна для выбранного клиента.");
                return new ProblemDetails(ValidationFailures);
            }
        }

        AppointmentRecurrenceRule? recurrenceRule = null;

        if (recurrenceType is not null)
        {
            recurrenceRule = new AppointmentRecurrenceRule
            {
                Id = Ulid.NewUlid(),
                Service = service,
                Client = client,
                Provider = provider,
                StartDate = req.StartDate,
                EndDate = req.PatternEndDate,
                RecurrenceType = recurrenceType,
                RecurrencePattern = req.RecurrencePattern
            };
        }

        var appointment = new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            Provider = provider,
            CourseTheme = courseTheme,
            CourseThemeId = courseTheme?.Id,
            LessonNotes = NormalizeLessonNotes(req.LessonNotes),
            StartDate = req.StartDate.ToUniversalTime(),
            EndDate = req.StartDate.AddHours(1).ToUniversalTime(),
            Status = AppointmentStatus.Planned,
            IsDeleted = false,
            RecurringRule = recurrenceRule
        };

        await db.AddAsync(appointment, ct);
        await db.SaveChangesAsync(ct);
        await auditLogService.WriteAsync(new AuditLogWriteRequest
        {
            Category = "schedule",
            Action = recurrenceRule is null ? "appointment_created" : "recurring_appointment_created",
            EntityType = "appointment",
            EntityId = appointment.Id.ToString(),
            Details = AuditDetailsFormatter.JoinChanges(
                AuditDetailsFormatter.DescribeContext("Клиент", $"{client.LastName} {client.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeContext("Услуга", service.Name),
                AuditDetailsFormatter.DescribeContext("Преподаватель", provider is null ? null : $"{provider.LastName} {provider.FirstName}".Trim()),
                AuditDetailsFormatter.DescribeContext("Тема курса", courseTheme?.Title),
                AuditDetailsFormatter.DescribeContext("Начало", appointment.StartDate),
                AuditDetailsFormatter.DescribeContext("Повторение", recurrenceRule?.RecurrenceType.DisplayName)
            )
        }, ct);

        if (reservationId is not null)
        {
            await requestReplayService.CompleteAsync(reservationId.Value, appointment.Id, ct);
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(ct);
        }

        return TypedResults.Created($"/appointments/{appointment.Id}", new CreateEntityResponse { Id = appointment.Id });
    }

    private static string? NormalizeLessonNotes(string? lessonNotes)
    {
        return string.IsNullOrWhiteSpace(lessonNotes) ? null : lessonNotes.Trim();
    }
}
