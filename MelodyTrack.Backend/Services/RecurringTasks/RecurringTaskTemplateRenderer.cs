namespace MelodyTrack.Backend.Services.RecurringTasks;

public sealed record RecurringTaskTemplateValues
{
    public string? ClientFirstName { get; init; }
    public string? ClientLastName { get; init; }
    public string? ClientPatronymic { get; init; }
    public string? TeacherFirstName { get; init; }
    public string? TeacherLastName { get; init; }
    public string? WhenWord { get; init; }
    public string? AppointmentStartTime { get; init; }
    public string? AppointmentDate { get; init; }
    public string? Date { get; init; }
}

public interface IRecurringTaskTemplateRenderer
{
    string Render(string template, RecurringTaskTemplateValues values);
}

public sealed class RecurringTaskTemplateRenderer : IRecurringTaskTemplateRenderer
{
    public string Render(string template, RecurringTaskTemplateValues values)
    {
        return template
            .Replace("{Client.FirstName}", values.ClientFirstName ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Client.LastName}", values.ClientLastName ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Client.Patronymic}", values.ClientPatronymic ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Teacher.FirstName}", values.TeacherFirstName ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Teacher.LastName}", values.TeacherLastName ?? string.Empty, StringComparison.Ordinal)
            .Replace("{When}", values.WhenWord ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Appointment.StartTime}", values.AppointmentStartTime ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Appointment.Date}", values.AppointmentDate ?? string.Empty, StringComparison.Ordinal)
            .Replace("{Date}", values.Date ?? values.AppointmentDate ?? string.Empty, StringComparison.Ordinal);
    }
}
