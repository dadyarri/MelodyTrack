using MelodyTrack.Backend.Validation;

namespace MelodyTrack.Backend.Api.Courses.Requests;

public class CreateCourseRequest : IValidatableRequest
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<CreateCourseLevelRequest> Levels { get; set; } = [];
    public List<CreateCourseBlockRequest> Blocks { get; set; } = [];
}

public class CreateCourseLevelRequest
{
    public required string Title { get; set; }
    public required int Order { get; set; }
    public required int RequiredExperiencePoints { get; set; }
}

public class CreateCourseBlockRequest
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required int Order { get; set; }
    public List<CreateCourseBranchRequest> Branches { get; set; } = [];
}

public class CreateCourseBranchRequest
{
    public required string Title { get; set; }
    public string? Description { get; set; }
    public required int Order { get; set; }
    public List<CreateCourseThemeRequest> Themes { get; set; } = [];
}

public class CreateCourseThemeRequest
{
    public required string Key { get; set; }
    public required string Title { get; set; }
    public string? Description { get; set; }
    public string? LessonContent { get; set; }
    public string? HomeworkDocument { get; set; }
    public List<CourseThemePracticeExerciseRequest> PracticeExercises { get; set; } = [];
    public required int Order { get; set; }
    public required int ExperiencePointsReward { get; set; }
    public List<string> DependencyKeys { get; set; } = [];
}

public class CourseThemePracticeExerciseRequest
{
    public required string Reference { get; set; }
    public required string Type { get; set; }
    public required string Configuration { get; set; }
}
