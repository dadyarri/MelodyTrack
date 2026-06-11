namespace MelodyTrack.Backend.Api.CourseEnrollments.Requests;

public class UpdateCourseEnrollmentThemeProgressRequest
{
    public required Ulid Id { get; set; }

    public required string Action { get; set; }
}

public enum CourseEnrollmentThemeProgressAction
{
    Unlock = 0,
    Start = 1,
    SendToHomework = 2,
    PassHomework = 3,
    ReturnToProgress = 4
}

public static class CourseEnrollmentThemeProgressActionExtensions
{
    public static bool TryParseApiKey(string? value, out CourseEnrollmentThemeProgressAction action)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "unlock":
                action = CourseEnrollmentThemeProgressAction.Unlock;
                return true;
            case "start":
                action = CourseEnrollmentThemeProgressAction.Start;
                return true;
            case "send-to-homework":
                action = CourseEnrollmentThemeProgressAction.SendToHomework;
                return true;
            case "pass-homework":
                action = CourseEnrollmentThemeProgressAction.PassHomework;
                return true;
            case "return-to-progress":
                action = CourseEnrollmentThemeProgressAction.ReturnToProgress;
                return true;
            default:
                action = default;
                return false;
        }
    }

    public static string ToAuditAction(this CourseEnrollmentThemeProgressAction action)
    {
        return action switch
        {
            CourseEnrollmentThemeProgressAction.Unlock => "course_theme_unlocked",
            CourseEnrollmentThemeProgressAction.Start => "course_theme_started",
            CourseEnrollmentThemeProgressAction.SendToHomework => "course_theme_sent_to_homework",
            CourseEnrollmentThemeProgressAction.PassHomework => "course_theme_homework_passed",
            CourseEnrollmentThemeProgressAction.ReturnToProgress => "course_theme_returned_to_progress",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    public static string ToDisplayName(this CourseEnrollmentThemeProgressAction action)
    {
        return action switch
        {
            CourseEnrollmentThemeProgressAction.Unlock => "Открыть",
            CourseEnrollmentThemeProgressAction.Start => "Начать",
            CourseEnrollmentThemeProgressAction.SendToHomework => "Отправить на ДЗ",
            CourseEnrollmentThemeProgressAction.PassHomework => "Принять ДЗ",
            CourseEnrollmentThemeProgressAction.ReturnToProgress => "Вернуть в работу",
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }
}
