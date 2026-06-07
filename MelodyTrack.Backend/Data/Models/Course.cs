using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class Course : BaseModel
{
    [MaxLength(200)]
    public required string Name { get; set; }

    [MaxLength(2000)]
    public string? Description { get; set; }

    public required DateTime CreatedAtUtc { get; set; }

    public required DateTime UpdatedAtUtc { get; set; }

    public List<CourseBlock> Blocks { get; set; } = [];
}
