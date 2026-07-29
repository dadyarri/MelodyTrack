using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Schedule;

internal static class AppointmentUpdateComparer
{
    public static bool IsNoOp(Appointment appointment, UpdateAppointmentRequest request)
    {
        var changes = new[]
        {
            IsClientChanged(appointment, request),
            IsServiceChanged(appointment, request),
            IsProviderChanged(appointment, request),
            IsCourseThemeChanged(appointment, request),
            AreLessonNotesChanged(appointment, request),
            IsStartDateChanged(appointment, request),
            IsStatusChanged(appointment, request),
            IsRecurrenceTypeChanged(appointment, request),
            IsRecurrencePatternChanged(appointment, request)
        };

        return !changes.Contains(true);
    }

    internal static bool IsClientChanged(Appointment appointment, UpdateAppointmentRequest request) =>
        request.ClientId is not null && request.ClientId != appointment.Client.Id;

    internal static bool IsServiceChanged(Appointment appointment, UpdateAppointmentRequest request) =>
        request.ServiceId is not null && request.ServiceId != appointment.Service.Id;

    internal static bool IsProviderChanged(Appointment appointment, UpdateAppointmentRequest request) =>
        request.ProviderId is not null && request.ProviderId != appointment.Provider?.Id;

    internal static bool IsCourseThemeChanged(Appointment appointment, UpdateAppointmentRequest request) =>
        request.HasCourseThemeSelection && request.CourseThemeId != appointment.CourseThemeId;

    internal static bool AreLessonNotesChanged(Appointment appointment, UpdateAppointmentRequest request) =>
        request.HasLessonNotes && NormalizeLessonNotes(request.LessonNotes) != appointment.LessonNotes;

    internal static bool IsStartDateChanged(Appointment appointment, UpdateAppointmentRequest request) =>
        request.StartDate is not null && request.StartDate != appointment.StartDate;

    internal static bool IsStatusChanged(Appointment appointment, UpdateAppointmentRequest request)
    {
        return request.Status is not null
               && (!AppointmentStatusExtensions.TryParseApiKey(request.Status, out var requestedStatus)
                   || requestedStatus != appointment.Status);
    }

    internal static bool IsRecurrenceTypeChanged(Appointment appointment, UpdateAppointmentRequest request) =>
        request.RecurrenceTypeId is not null
        && request.RecurrenceTypeId != appointment.RecurringRule?.RecurrenceType.Id;

    internal static bool IsRecurrencePatternChanged(Appointment appointment, UpdateAppointmentRequest request) =>
        request.RecurrencePattern is not null
        && request.RecurrencePattern != appointment.RecurringRule?.RecurrencePattern;

    internal static string? NormalizeLessonNotes(string? lessonNotes) =>
        string.IsNullOrWhiteSpace(lessonNotes) ? null : lessonNotes.Trim();
}
