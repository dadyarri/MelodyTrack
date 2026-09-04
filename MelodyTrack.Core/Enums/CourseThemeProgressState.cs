namespace MelodyTrack.Backend.Data.Enums;

public enum CourseThemeProgressState
{
    BlockedByDependency = 0,
    AvailableToUnlock = 1,
    Unlocked = 2,
    InProgress = 3,
    WaitingForHomework = 4,
    Completed = 5
}
