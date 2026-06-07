using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Api.CourseEnrollments.Responses;

public class GetCourseEnrollmentsResponse
{
    public required List<CourseEnrollmentDto> Enrollments { get; set; }
}

public class CourseEnrollmentDto
{
    public required Ulid Id { get; set; }
    public required Ulid ClientId { get; set; }
    public required string ClientDisplayName { get; set; }
    public required Ulid CourseId { get; set; }
    public required string CourseName { get; set; }
    public required DateTime CreatedAtUtc { get; set; }
    public required int EarnedEvolutionPoints { get; set; }
    public required int SpentEvolutionPoints { get; set; }
    public required int EarnedExperiencePoints { get; set; }
    public required List<CourseEnrollmentThemeDto> Themes { get; set; }
}

public class CourseEnrollmentThemeDto
{
    public required Ulid Id { get; set; }
    public required Ulid CourseThemeId { get; set; }
    public required string ThemeTitle { get; set; }
    public required CourseThemeProgressState State { get; set; }
    public DateTime? UnlockedAtUtc { get; set; }
    public DateTime? StartedAtUtc { get; set; }
    public DateTime? WaitingForHomeworkAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public required int SpentEvolutionPoints { get; set; }
    public required int EarnedEvolutionPoints { get; set; }
    public required int EarnedExperiencePoints { get; set; }
}
