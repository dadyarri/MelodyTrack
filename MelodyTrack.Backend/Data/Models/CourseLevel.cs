using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Backend.Data.Models;

public class CourseLevel : BaseModel
{
    public required Ulid CourseId { get; set; }

    public required Course Course { get; set; }

    public required int Order { get; set; }

    [MaxLength(200)]
    public required string Title { get; set; }

    public required int RequiredExperiencePoints { get; set; }
}
