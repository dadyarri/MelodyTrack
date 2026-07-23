using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class CourseBlock : BaseModel
{
    public required Ulid CourseId { get; set; }

    public required Course Course { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    public required int Order { get; set; }

    public List<CourseBranch> Branches { get; set; } = [];
}
