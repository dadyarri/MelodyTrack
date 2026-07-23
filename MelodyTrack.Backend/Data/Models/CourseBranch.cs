using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class CourseBranch : BaseModel
{
    public required Ulid BlockId { get; set; }

    public required CourseBlock Block { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    public required int Order { get; set; }

    public List<CourseTheme> Themes { get; set; } = [];
}
