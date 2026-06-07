using MelodyTrack.Backend.Api.Courses.Requests;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Courses.Endpoints;

internal static class CourseStructureBuilder
{
    public static void PopulateCourse(Course course, IEnumerable<CreateCourseBlockRequest>? blocks)
    {
        var themeByKey = new Dictionary<string, CourseTheme>(StringComparer.OrdinalIgnoreCase);
        var dependencyKeysByThemeId = new Dictionary<Ulid, List<string>>();

        foreach (var blockRequest in (blocks ?? []).OrderBy(block => block.Order))
        {
            var block = new CourseBlock
            {
                Id = Ulid.NewUlid(),
                Course = course,
                CourseId = course.Id,
                Title = blockRequest.Title,
                Description = blockRequest.Description,
                Order = blockRequest.Order
            };

            foreach (var branchRequest in (blockRequest.Branches ?? []).OrderBy(branch => branch.Order))
            {
                var branch = new CourseBranch
                {
                    Id = Ulid.NewUlid(),
                    Block = block,
                    BlockId = block.Id,
                    Title = branchRequest.Title,
                    Description = branchRequest.Description,
                    Order = branchRequest.Order
                };

                foreach (var themeRequest in (branchRequest.Themes ?? []).OrderBy(theme => theme.Order))
                {
                    var theme = new CourseTheme
                    {
                        Id = Ulid.NewUlid(),
                        Branch = branch,
                        BranchId = branch.Id,
                        Title = themeRequest.Title,
                        Description = themeRequest.Description,
                        LessonContent = themeRequest.LessonContent,
                        HomeworkContent = themeRequest.HomeworkContent,
                        Order = themeRequest.Order,
                        UnlockCostPoints = themeRequest.UnlockCostPoints,
                        EvolutionPointsReward = themeRequest.EvolutionPointsReward,
                        ExperiencePointsReward = themeRequest.ExperiencePointsReward
                    };

                    branch.Themes.Add(theme);
                    themeByKey[themeRequest.Key] = theme;
                    dependencyKeysByThemeId[theme.Id] = themeRequest.DependencyKeys
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }

                block.Branches.Add(branch);
            }

            course.Blocks.Add(block);
        }

        foreach (var theme in course.Blocks.SelectMany(block => block.Branches).SelectMany(branch => branch.Themes))
        {
            foreach (var dependencyKey in dependencyKeysByThemeId[theme.Id])
            {
                theme.Dependencies.Add(new CourseThemeDependency
                {
                    Id = Ulid.NewUlid(),
                    Theme = theme,
                    ThemeId = theme.Id,
                    DependsOnTheme = themeByKey[dependencyKey],
                    DependsOnThemeId = themeByKey[dependencyKey].Id
                });
            }
        }
    }
}
