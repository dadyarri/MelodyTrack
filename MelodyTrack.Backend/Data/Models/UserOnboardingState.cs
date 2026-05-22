using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Data.Models;

public class UserOnboardingState : BaseModel
{
    public required Ulid UserId { get; set; }
    public required User User { get; set; }
    public required string CurrentStep { get; set; }
    public required string CurrentPath { get; set; }
    public required OnboardingStatus Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
}
