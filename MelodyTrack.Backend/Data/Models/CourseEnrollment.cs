namespace MelodyTrack.Backend.Data.Models;

public class CourseEnrollment : BaseModel
{
    public required Ulid ClientId { get; set; }

    public required Client Client { get; set; }

    public required Ulid CourseId { get; set; }

    public required Course Course { get; set; }

    public required DateTime CreatedAtUtc { get; set; }

    public required DateTime UpdatedAtUtc { get; set; }

    public required int EarnedExperiencePoints { get; set; }

    public List<CourseEnrollmentTheme> Themes { get; set; } = [];
}
