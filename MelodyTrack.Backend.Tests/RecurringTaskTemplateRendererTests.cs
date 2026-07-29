using MelodyTrack.Backend.Services.RecurringTasks;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class RecurringTaskTemplateRendererTests
{
    private readonly RecurringTaskTemplateRenderer _renderer = new();

    [Fact]
    public void Render_ReplacesSupportedTokens()
    {
        var result = _renderer.Render(
            "{Client.LastName} {Client.FirstName} {Client.Patronymic}; "
            + "{Teacher.LastName} {Teacher.FirstName}; {When}; "
            + "{Appointment.Date} {Appointment.StartTime}; {Date}",
            new RecurringTaskTemplateValues
            {
                ClientFirstName = "Иван",
                ClientLastName = "Петров",
                ClientPatronymic = "Сергеевич",
                TeacherFirstName = "Анна",
                TeacherLastName = "Смирнова",
                WhenWord = "завтра",
                AppointmentDate = "25.07.2026",
                AppointmentStartTime = "18:30",
                Date = "24.07.2026"
            });

        result.ShouldBe(
            "Петров Иван Сергеевич; Смирнова Анна; завтра; "
            + "25.07.2026 18:30; 24.07.2026");
    }

    [Fact]
    public void Render_ReplacesMissingValuesWithEmptyStrings()
    {
        var result = _renderer.Render(
            "{Client.FirstName}|{Teacher.FirstName}|{When}",
            new RecurringTaskTemplateValues());

        result.ShouldBe("||");
    }

    [Fact]
    public void Render_UsesAppointmentDateAsDateFallback()
    {
        var result = _renderer.Render(
            "{Appointment.Date}|{Date}",
            new RecurringTaskTemplateValues
            {
                AppointmentDate = "25.07.2026"
            });

        result.ShouldBe("25.07.2026|25.07.2026");
    }
}
