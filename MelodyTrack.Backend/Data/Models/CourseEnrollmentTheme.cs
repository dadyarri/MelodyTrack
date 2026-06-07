using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Data.Models;

public class CourseEnrollmentTheme : BaseModel
{
    public required Ulid EnrollmentId { get; set; }

    public required CourseEnrollment Enrollment { get; set; }

    public required Ulid CourseThemeId { get; set; }

    public required CourseTheme CourseTheme { get; set; }

    public required CourseThemeProgressState State { get; set; }

    public DateTime? UnlockedAtUtc { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? WaitingForHomeworkAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public required int SpentEvolutionPoints { get; set; }

    public required int EarnedEvolutionPoints { get; set; }

    public required int EarnedExperiencePoints { get; set; }
}
