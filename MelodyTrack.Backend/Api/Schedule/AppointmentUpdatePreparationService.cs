using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Data;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Backend.Services;
using Microsoft.EntityFrameworkCore;

namespace MelodyTrack.Backend.Api.Schedule;

public sealed class AppointmentUpdatePreparationService(AppDbContext db, ISchedulingAvailabilityService availabilityService, IRecurringAppointmentService recurrence)
{
    internal async Task<AppointmentUpdatePreparationResult> PrepareAsync(
        Appointment appointment,
        UpdateAppointmentRequest request,
        CancellationToken ct)
    {
        if (appointment.RecurringRule is not null
            && (AppointmentUpdateComparer.IsRecurrenceTypeChanged(appointment, request)
                || AppointmentUpdateComparer.IsRecurrencePatternChanged(appointment, request)))
        {
            return AppointmentUpdatePreparationResult.Failed(AppointmentUpdatePreparationError.RecurringPatternChange);
        }

        var statusResult = ResolveStatus(request.Status);
        if (statusResult.Error != AppointmentUpdatePreparationError.None)
        {
            return AppointmentUpdatePreparationResult.Failed(statusResult.Error);
        }

        var changes = AppointmentUpdateChanges.Detect(appointment, request);
        var nextStartDate = request.StartDate ?? appointment.StartDate;
        var duration = appointment.EndDate - appointment.StartDate;

        var referenceError = await ApplyReferencesAsync(appointment, request, ct);
        if (referenceError != AppointmentUpdatePreparationError.None)
        {
            return AppointmentUpdatePreparationResult.Failed(referenceError);
        }

        if (request.HasLessonNotes)
        {
            appointment.LessonNotes = AppointmentUpdateComparer.NormalizeLessonNotes(request.LessonNotes);
        }

        var availabilityError = await ValidateAvailabilityAsync(appointment, request, changes, nextStartDate, duration, ct);
        return availabilityError == AppointmentUpdatePreparationError.None
            ? new AppointmentUpdatePreparationResult(statusResult.Status, changes, nextStartDate, duration, ParseScope(request.Scope), availabilityError)
            : AppointmentUpdatePreparationResult.Failed(availabilityError);
    }

    internal async Task<AppointmentUpdatePreparationError> ApplyRecurrenceAsync(
        Appointment appointment,
        UpdateAppointmentRequest request,
        CancellationToken ct)
    {
        if (request.RecurrenceTypeId is null || appointment.RecurringRule is not null)
        {
            return AppointmentUpdatePreparationError.None;
        }

        if (request.RecurrencePattern is null)
        {
            return AppointmentUpdatePreparationError.MissingRecurrencePattern;
        }

        if (request.StartDate is null)
        {
            return AppointmentUpdatePreparationError.MissingRecurrenceStartDate;
        }

        var recurrenceType = await db.RecurrenceTypes.FirstOrDefaultAsync(item => item.Id == request.RecurrenceTypeId.Value, ct);
        if (recurrenceType is null)
        {
            return AppointmentUpdatePreparationError.RecurrenceTypeNotFound;
        }

        if (recurrenceType.Type == AppointmentRecurrenceType.Weekly && request.RecurrencePattern is not (>= 1 and <= 127)
            || recurrenceType.Type == AppointmentRecurrenceType.Monthly && request.RecurrencePattern is not (>= 1 and <= 31))
        {
            return AppointmentUpdatePreparationError.InvalidRecurrencePattern;
        }

        var recurrenceRule = new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(),
            RecurrenceType = recurrenceType,
            RecurrencePattern = request.RecurrencePattern,
            Client = appointment.Client,
            Service = appointment.Service,
            Provider = appointment.Provider,
            Timezone = appointment.Provider?.ScheduleTimezone ?? request.Timezone ?? "UTC",
            DurationMinutes = (int)(appointment.EndDate - appointment.StartDate).TotalMinutes,
            StartDate = request.StartDate.Value
        };

        appointment.RecurringRule = recurrenceRule;
        appointment.RecurringRuleId = recurrenceRule.Id;
        appointment.RecurrenceOrdinal = recurrence.GetOrdinal(recurrenceRule, appointment.StartDate);
        return AppointmentUpdatePreparationError.None;
    }

    private async Task<AppointmentUpdatePreparationError> ApplyReferencesAsync(
        Appointment appointment,
        UpdateAppointmentRequest request,
        CancellationToken ct)
    {
        var clientError = await ApplyClientAsync(appointment, request.ClientId, ct);
        if (clientError != AppointmentUpdatePreparationError.None)
        {
            return clientError;
        }

        var serviceError = await ApplyServiceAsync(appointment, request.ServiceId, ct);
        if (serviceError != AppointmentUpdatePreparationError.None)
        {
            return serviceError;
        }

        var providerError = await ApplyProviderAsync(appointment, request.ProviderId, ct);
        return providerError != AppointmentUpdatePreparationError.None
            ? providerError
            : await ApplyCourseThemeAsync(appointment, request, ct);
    }

    private async Task<AppointmentUpdatePreparationError> ApplyClientAsync(Appointment appointment, Ulid? clientId, CancellationToken ct)
    {
        if (clientId is null)
        {
            return AppointmentUpdatePreparationError.None;
        }

        var client = await db.Clients.FirstOrDefaultAsync(item => item.Id == clientId.Value, ct);
        if (client is null)
        {
            return AppointmentUpdatePreparationError.ClientNotFound;
        }

        appointment.Client = client;
        return AppointmentUpdatePreparationError.None;
    }

    private async Task<AppointmentUpdatePreparationError> ApplyServiceAsync(Appointment appointment, Ulid? serviceId, CancellationToken ct)
    {
        if (serviceId is null)
        {
            return AppointmentUpdatePreparationError.None;
        }

        var service = await db.Services.FirstOrDefaultAsync(item => item.Id == serviceId.Value, ct);
        if (service is null)
        {
            return AppointmentUpdatePreparationError.ServiceNotFound;
        }

        appointment.Service = service;
        return AppointmentUpdatePreparationError.None;
    }

    private async Task<AppointmentUpdatePreparationError> ApplyProviderAsync(Appointment appointment, Ulid? providerId, CancellationToken ct)
    {
        if (providerId is null)
        {
            return AppointmentUpdatePreparationError.None;
        }

        var provider = await db.Users.FirstOrDefaultAsync(item => item.Id == providerId.Value, ct);
        if (provider is null)
        {
            return AppointmentUpdatePreparationError.ProviderNotFound;
        }

        appointment.Provider = provider;
        return AppointmentUpdatePreparationError.None;
    }

    private async Task<AppointmentUpdatePreparationError> ApplyCourseThemeAsync(
        Appointment appointment,
        UpdateAppointmentRequest request,
        CancellationToken ct)
    {
        if (!request.HasCourseThemeSelection)
        {
            return AppointmentUpdatePreparationError.None;
        }

        if (request.CourseThemeId is null)
        {
            appointment.CourseTheme = null;
            appointment.CourseThemeId = null;
            return AppointmentUpdatePreparationError.None;
        }

        var courseTheme = await db.CourseThemes
            .Include(item => item.Branch)
                .ThenInclude(item => item.Block)
                    .ThenInclude(item => item.Course)
            .FirstOrDefaultAsync(item => item.Id == request.CourseThemeId.Value, ct);
        if (courseTheme is null)
        {
            return AppointmentUpdatePreparationError.CourseThemeNotFound;
        }

        var hasEnrollment = await db.CourseEnrollments.AsNoTracking()
            .AnyAsync(item => item.ClientId == appointment.Client.Id && item.CourseId == courseTheme.Branch.Block.CourseId, ct);
        if (!hasEnrollment)
        {
            return AppointmentUpdatePreparationError.CourseThemeUnavailable;
        }

        appointment.CourseTheme = courseTheme;
        appointment.CourseThemeId = courseTheme.Id;
        return AppointmentUpdatePreparationError.None;
    }

    private async Task<AppointmentUpdatePreparationError> ValidateAvailabilityAsync(
        Appointment appointment,
        UpdateAppointmentRequest request,
        AppointmentUpdateChanges changes,
        DateTime nextStartDate,
        TimeSpan duration,
        CancellationToken ct)
    {
        if (appointment.RecurringRule is not null && changes.StartDateChanged && ParseScope(request.Scope) != AppointmentUpdateScope.Single)
            return AppointmentUpdatePreparationError.None;
        var nextStatus = ResolveStatus(request.Status).Status ?? appointment.Status;
        if (nextStatus != AppointmentStatus.Planned || (!changes.ProviderChanged && !changes.ClientChanged && !changes.StartDateChanged
            && appointment.Status == AppointmentStatus.Planned))
        {
            return AppointmentUpdatePreparationError.None;
        }
        var blockers = await availabilityService.EvaluateAsync(appointment.Client.Id, appointment.Provider?.Id,
            nextStartDate, nextStartDate.Add(duration), [appointment.Id], ct);
        return blockers.Count == 0 ? AppointmentUpdatePreparationError.None : AppointmentUpdatePreparationError.ProviderUnavailable;
    }

    private static (AppointmentStatus? Status, AppointmentUpdatePreparationError Error) ResolveStatus(string? status)
    {
        if (status is null)
        {
            return (null, AppointmentUpdatePreparationError.None);
        }

        return AppointmentStatusExtensions.TryParseApiKey(status, out var parsed)
            ? (parsed, AppointmentUpdatePreparationError.None)
            : (null, AppointmentUpdatePreparationError.InvalidStatus);
    }

    private static AppointmentUpdateScope ParseScope(string? scope) =>
        scope?.Trim().ToLowerInvariant() switch
        {
            "this-and-following" => AppointmentUpdateScope.ThisAndFollowing,
            "all" => AppointmentUpdateScope.All,
            _ => AppointmentUpdateScope.Single
        };
}

internal sealed record AppointmentUpdatePreparationResult(
    AppointmentStatus? RequestedStatus,
    AppointmentUpdateChanges Changes,
    DateTime NextStartDate,
    TimeSpan Duration,
    AppointmentUpdateScope Scope,
    AppointmentUpdatePreparationError Error)
{
    public static AppointmentUpdatePreparationResult Failed(AppointmentUpdatePreparationError error) =>
        new(null, AppointmentUpdateChanges.None, default, default, AppointmentUpdateScope.Single, error);
}

internal sealed record AppointmentUpdateChanges(
    bool ClientChanged,
    bool ServiceChanged,
    bool ProviderChanged,
    bool StartDateChanged,
    bool CourseThemeChanged,
    bool LessonNotesChanged)
{
    public static readonly AppointmentUpdateChanges None = new(false, false, false, false, false, false);

    public bool RequiresRecurringDetachment =>
        ClientChanged || ServiceChanged || ProviderChanged || StartDateChanged || CourseThemeChanged || LessonNotesChanged;

    public static AppointmentUpdateChanges Detect(Appointment appointment, UpdateAppointmentRequest request) => new(
        AppointmentUpdateComparer.IsClientChanged(appointment, request),
        AppointmentUpdateComparer.IsServiceChanged(appointment, request),
        AppointmentUpdateComparer.IsProviderChanged(appointment, request),
        AppointmentUpdateComparer.IsStartDateChanged(appointment, request),
        AppointmentUpdateComparer.IsCourseThemeChanged(appointment, request),
        AppointmentUpdateComparer.AreLessonNotesChanged(appointment, request));
}

internal enum AppointmentUpdatePreparationError
{
    None,
    InvalidStatus,
    ClientNotFound,
    ServiceNotFound,
    ProviderNotFound,
    CourseThemeNotFound,
    CourseThemeUnavailable,
    MissingTimezone,
    ProviderUnavailable,
    MissingRecurrencePattern,
    MissingRecurrenceStartDate,
    RecurrenceTypeNotFound,
    InvalidRecurrencePattern,
    RecurringPatternChange
}

internal enum AppointmentUpdateScope
{
    Single,
    ThisAndFollowing,
    All
}
