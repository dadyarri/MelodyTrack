using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using MelodyTrack.Backend.Validation;
using MelodyTrack.Core.Auditing;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Requests;

public class UpdateCourseEnrollmentThemeProgressRequest : IValidatableRequest
{
    [JsonIgnore]
    public Ulid Id { get; set; }

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

    public static AuditEventDefinition ToAuditEvent(this CourseEnrollmentThemeProgressAction action)
    {
        return action switch
        {
            CourseEnrollmentThemeProgressAction.Unlock => AuditCatalog.Events.CourseThemeUnlocked,
            CourseEnrollmentThemeProgressAction.Start => AuditCatalog.Events.CourseThemeStarted,
            CourseEnrollmentThemeProgressAction.SendToHomework => AuditCatalog.Events.CourseThemeSentToHomework,
            CourseEnrollmentThemeProgressAction.PassHomework => AuditCatalog.Events.CourseThemeHomeworkPassed,
            CourseEnrollmentThemeProgressAction.ReturnToProgress => AuditCatalog.Events.CourseThemeReturnedToProgress,
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
