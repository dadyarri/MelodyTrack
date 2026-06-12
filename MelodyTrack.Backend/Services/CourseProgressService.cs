using MelodyTrack.Backend.Data.Enums;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Services;

public class CourseProgressService
{
    public int GetAvailableEvolutionPoints(CourseEnrollment enrollment)
    {
        return enrollment.EarnedEvolutionPoints - enrollment.SpentEvolutionPoints;
    }

    public void RefreshAvailability(CourseEnrollment enrollment, DateTime nowUtc)
    {
        var themesByCourseThemeId = enrollment.Themes.ToDictionary(item => item.CourseThemeId);

        foreach (var theme in GetOrderedThemes(enrollment))
        {
            if (theme.State is CourseThemeProgressState.Completed
                or CourseThemeProgressState.InProgress
                or CourseThemeProgressState.WaitingForHomework)
            {
                continue;
            }

            if (!IsEligible(theme, themesByCourseThemeId))
            {
                theme.State = CourseThemeProgressState.BlockedByDependency;
                continue;
            }

            if (theme.State == CourseThemeProgressState.Unlocked)
            {
                continue;
            }

            if (theme.CourseTheme.UnlockCostPoints == 0)
            {
                theme.State = CourseThemeProgressState.Unlocked;
                theme.UnlockedAtUtc ??= nowUtc;
                continue;
            }

            theme.State = CourseThemeProgressState.AvailableToUnlock;
        }
    }

    private static List<CourseEnrollmentTheme> GetOrderedThemes(CourseEnrollment enrollment)
    {
        return enrollment.Themes
            .OrderBy(item => item.CourseTheme.Branch.Block.Order)
            .ThenBy(item => item.CourseTheme.Branch.Order)
            .ThenBy(item => item.CourseTheme.Order)
            .ToList();
    }

    public bool IsEligibleForProgress(CourseEnrollment enrollment, CourseEnrollmentTheme theme)
    {
        var themesByCourseThemeId = enrollment.Themes.ToDictionary(item => item.CourseThemeId);

        return IsEligible(theme, themesByCourseThemeId);
    }

    private static bool IsEligible(
        CourseEnrollmentTheme theme,
        IReadOnlyDictionary<Ulid, CourseEnrollmentTheme> themesByCourseThemeId)
    {
        if (!AreDependenciesCompleted(theme, themesByCourseThemeId))
        {
            return false;
        }

        if (!IsPreviousBranchThemeCompleted(theme, themesByCourseThemeId))
        {
            return false;
        }

        return ArePreviousBlocksCompleted(theme, themesByCourseThemeId);
    }

    private static bool AreDependenciesCompleted(
        CourseEnrollmentTheme theme,
        IReadOnlyDictionary<Ulid, CourseEnrollmentTheme> themesByCourseThemeId)
    {
        foreach (var dependency in theme.CourseTheme.Dependencies)
        {
            if (!themesByCourseThemeId.TryGetValue(dependency.DependsOnThemeId, out var dependencyTheme)
                || dependencyTheme.State != CourseThemeProgressState.Completed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsPreviousBranchThemeCompleted(
        CourseEnrollmentTheme theme,
        IReadOnlyDictionary<Ulid, CourseEnrollmentTheme> themesByCourseThemeId)
    {
        var previousTheme = theme.CourseTheme.Branch.Themes
            .Where(item => item.Order < theme.CourseTheme.Order)
            .OrderByDescending(item => item.Order)
            .FirstOrDefault();

        if (previousTheme is null)
        {
            return true;
        }

        return themesByCourseThemeId.TryGetValue(previousTheme.Id, out var previousEnrollmentTheme)
            && previousEnrollmentTheme.State == CourseThemeProgressState.Completed;
    }

    private static bool ArePreviousBlocksCompleted(
        CourseEnrollmentTheme theme,
        IReadOnlyDictionary<Ulid, CourseEnrollmentTheme> themesByCourseThemeId)
    {
        var currentBlockOrder = theme.CourseTheme.Branch.Block.Order;

        return themesByCourseThemeId.Values
            .Where(item => item.CourseTheme.Branch.Block.Order < currentBlockOrder)
            .All(item => item.State == CourseThemeProgressState.Completed);
    }
}
