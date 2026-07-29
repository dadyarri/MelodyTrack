using MelodyTrack.Backend.Api.Schedule;
using MelodyTrack.Backend.Api.Schedule.Requests;
using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class AppointmentUpdateComparerTests
{
    public enum UpdatedField
    {
        Client,
        Service,
        Provider,
        CourseTheme,
        LessonNotes,
        StartDate,
        Status,
        RecurrenceType,
        RecurrencePattern
    }

    [Fact]
    public void IsNoOp_WithNoFieldsProvided_ReturnsTrue()
    {
        AppointmentUpdateComparer.IsNoOp(CreateAppointment(), new UpdateAppointmentRequest()).ShouldBeTrue();
    }

    [Theory]
    [InlineData(UpdatedField.Client)]
    [InlineData(UpdatedField.Service)]
    [InlineData(UpdatedField.Provider)]
    [InlineData(UpdatedField.CourseTheme)]
    [InlineData(UpdatedField.LessonNotes)]
    [InlineData(UpdatedField.StartDate)]
    [InlineData(UpdatedField.Status)]
    [InlineData(UpdatedField.RecurrenceType)]
    [InlineData(UpdatedField.RecurrencePattern)]
    public void IsNoOp_WithSameExplicitFieldValue_ReturnsTrue(UpdatedField field)
    {
        var appointment = CreateAppointment();
        var request = CreateFieldRequest(appointment, field, changed: false);

        AppointmentUpdateComparer.IsNoOp(appointment, request).ShouldBeTrue();
    }

    [Theory]
    [InlineData(UpdatedField.Client)]
    [InlineData(UpdatedField.Service)]
    [InlineData(UpdatedField.Provider)]
    [InlineData(UpdatedField.CourseTheme)]
    [InlineData(UpdatedField.LessonNotes)]
    [InlineData(UpdatedField.StartDate)]
    [InlineData(UpdatedField.Status)]
    [InlineData(UpdatedField.RecurrenceType)]
    [InlineData(UpdatedField.RecurrencePattern)]
    public void IsNoOp_WithChangedFieldValue_ReturnsFalse(UpdatedField field)
    {
        var appointment = CreateAppointment();
        var request = CreateFieldRequest(appointment, field, changed: true);

        AppointmentUpdateComparer.IsNoOp(appointment, request).ShouldBeFalse();
    }

    [Fact]
    public void IsNoOp_NormalizesExplicitLessonNotesBeforeComparison()
    {
        var appointment = CreateAppointment();
        var request = new UpdateAppointmentRequest { HasLessonNotes = true, LessonNotes = "  lesson notes  " };

        AppointmentUpdateComparer.IsNoOp(appointment, request).ShouldBeTrue();
    }

    [Fact]
    public void IsNoOp_TreatsInvalidStatusAsAChange()
    {
        var appointment = CreateAppointment();
        var request = new UpdateAppointmentRequest { Status = "not-a-status" };

        AppointmentUpdateComparer.IsNoOp(appointment, request).ShouldBeFalse();
    }

    private static UpdateAppointmentRequest CreateFieldRequest(Appointment appointment, UpdatedField field, bool changed)
    {
        var differentId = Ulid.NewUlid();
        return field switch
        {
            UpdatedField.Client => new UpdateAppointmentRequest { ClientId = changed ? differentId : appointment.Client.Id },
            UpdatedField.Service => new UpdateAppointmentRequest { ServiceId = changed ? differentId : appointment.Service.Id },
            UpdatedField.Provider => new UpdateAppointmentRequest { ProviderId = changed ? differentId : appointment.Provider?.Id },
            UpdatedField.CourseTheme => new UpdateAppointmentRequest
            {
                HasCourseThemeSelection = true,
                CourseThemeId = changed ? differentId : appointment.CourseThemeId
            },
            UpdatedField.LessonNotes => new UpdateAppointmentRequest
            {
                HasLessonNotes = true,
                LessonNotes = changed ? "different notes" : appointment.LessonNotes
            },
            UpdatedField.StartDate => new UpdateAppointmentRequest
            {
                StartDate = changed ? appointment.StartDate.AddMinutes(1) : appointment.StartDate
            },
            UpdatedField.Status => new UpdateAppointmentRequest
            {
                Status = changed ? AppointmentStatus.Completed.ToApiKey() : appointment.Status.ToApiKey()
            },
            UpdatedField.RecurrenceType => new UpdateAppointmentRequest
            {
                RecurrenceTypeId = changed ? differentId : appointment.RecurringRule!.RecurrenceType.Id
            },
            UpdatedField.RecurrencePattern => new UpdateAppointmentRequest
            {
                RecurrencePattern = changed ? 2 : appointment.RecurringRule!.RecurrencePattern
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null)
        };
    }

    private static Appointment CreateAppointment()
    {
        var client = new Client
        {
            Id = Ulid.NewUlid(),
            FirstName = "Client",
            LastName = "One",
            CreatedAtUtc = DateTime.UtcNow,
            Contacts = new ClientContacts { Id = Ulid.NewUlid() }
        };
        var service = new Service { Id = Ulid.NewUlid(), Name = "Lesson" };
        var recurrenceType = new RecurrenceType
        {
            Id = Ulid.NewUlid(),
            DisplayName = "Daily",
            Type = AppointmentRecurrenceType.Daily
        };
        var recurringRule = new AppointmentRecurrenceRule
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            StartDate = new DateTime(2026, 7, 1, 10, 0, 0, DateTimeKind.Utc),
            RecurrenceType = recurrenceType,
            RecurrencePattern = 1
        };

        return new Appointment
        {
            Id = Ulid.NewUlid(),
            Client = client,
            Service = service,
            CourseThemeId = Ulid.NewUlid(),
            LessonNotes = "lesson notes",
            StartDate = recurringRule.StartDate,
            EndDate = recurringRule.StartDate.AddHours(1),
            Status = AppointmentStatus.Planned,
            RecurringRule = recurringRule,
            IsDeleted = false
        };
    }
}
