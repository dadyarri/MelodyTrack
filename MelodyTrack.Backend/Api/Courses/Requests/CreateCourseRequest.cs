namespace MelodyTrack.Backend.Api.Courses.Requests;

public class CreateCourseRequest
{
    public required string Name { get; set; }
    public string? Description { get; set; }
    public List<CreateCourseBlockRequest> Blocks { get; set; } = [];
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
    public string? HomeworkContent { get; set; }
    public required int Order { get; set; }
    public required int UnlockCostPoints { get; set; }
    public required int EvolutionPointsReward { get; set; }
    public required int ExperiencePointsReward { get; set; }
    public List<string> DependencyKeys { get; set; } = [];
}
