using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class CourseTheme : BaseModel
{
    public required Ulid BranchId { get; set; }

    public required CourseBranch Branch { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    [MaxLength(4000)]
    public string? Description { get; set; }

    public string? LessonContent { get; set; }

    public string? HomeworkContent { get; set; }

    public required int Order { get; set; }

    public required int UnlockCostPoints { get; set; }

    public required int EvolutionPointsReward { get; set; }

    public required int ExperiencePointsReward { get; set; }

    public List<CourseThemeDependency> Dependencies { get; set; } = [];

    public List<CourseThemeDependency> RequiredForThemes { get; set; } = [];
}
