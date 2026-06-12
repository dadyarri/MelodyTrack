using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Api.Courses.Responses;

public class GetCourseResponse
{
    public required CourseDto Course { get; set; }
}

public class GetCoursesResponse
{
    public required List<CourseSummaryDto> Courses { get; set; }
}

public class CourseSummaryDto
{
    public required Ulid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required int BlockCount { get; set; }
    public required int ThemeCount { get; set; }
    public required DateTime UpdatedAtUtc { get; set; }
}

public class CourseDto
{
    public required Ulid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public required DateTime CreatedAtUtc { get; set; }
    public required DateTime UpdatedAtUtc { get; set; }
    public required List<CourseBlockDto> Blocks { get; set; }
}

public class CourseBlockDto
{
    public required Ulid Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required int Order { get; set; }
    public required List<CourseBranchDto> Branches { get; set; }
}

public class CourseBranchDto
{
    public required Ulid Id { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required int Order { get; set; }
    public required List<CourseThemeDto> Themes { get; set; }
}

public class CourseThemeDto
{
    public required Ulid Id { get; set; }
    public required string Key { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? LessonContent { get; set; }
    public string? HomeworkContent { get; set; }
    public required int Order { get; set; }
    public required int UnlockCostPoints { get; set; }
    public required int EvolutionPointsReward { get; set; }
    public required int ExperiencePointsReward { get; set; }
    public required List<Ulid> DependencyThemeIds { get; set; }
}
