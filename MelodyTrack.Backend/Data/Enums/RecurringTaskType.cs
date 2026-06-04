namespace MelodyTrack.Backend.Data.Enums;

public enum RecurringTaskType
{
    AppointmentReminder = 0,
    BirthdayGreeting = 1,
    TrialFollowUp = 2,
    InactiveClientReminder = 3,
    TeacherDailySchedule = 4
}

public static class RecurringTaskTypeExtensions
{
    public static string ToApiKey(this RecurringTaskType type)
    {
        return type switch
        {
            RecurringTaskType.AppointmentReminder => "appointment-reminder",
            RecurringTaskType.BirthdayGreeting => "birthday-greeting",
            RecurringTaskType.TrialFollowUp => "trial-follow-up",
            RecurringTaskType.InactiveClientReminder => "inactive-client-reminder",
            RecurringTaskType.TeacherDailySchedule => "teacher-daily-schedule",
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    public static bool TryParseApiKey(string? value, out RecurringTaskType type)
    {
        type = RecurringTaskType.AppointmentReminder;

        switch (value?.Trim().ToLowerInvariant())
        {
            case "appointment-reminder":
                type = RecurringTaskType.AppointmentReminder;
                return true;
            case "birthday-greeting":
                type = RecurringTaskType.BirthdayGreeting;
                return true;
            case "trial-follow-up":
                type = RecurringTaskType.TrialFollowUp;
                return true;
            case "inactive-client-reminder":
                type = RecurringTaskType.InactiveClientReminder;
                return true;
            case "teacher-daily-schedule":
                type = RecurringTaskType.TeacherDailySchedule;
                return true;
            default:
                return false;
        }
    }
}
