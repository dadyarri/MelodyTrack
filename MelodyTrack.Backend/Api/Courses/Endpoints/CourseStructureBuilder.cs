using MelodyTrack.Backend.Api.Courses.Requests;
using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Courses.Endpoints;

internal static class CourseStructureBuilder
{
    public static void PopulateCourse(
        Course course,
        IEnumerable<CreateCourseBlockRequest>? blocks,
        IReadOnlyDictionary<string, CourseTheme>? existingThemesByKey = null)
    {
        var themeByKey = new Dictionary<string, CourseTheme>(StringComparer.OrdinalIgnoreCase);
        var dependencyKeysByThemeId = new Dictionary<Ulid, List<string>>();
        var reusableThemes = existingThemesByKey is null
            ? new Dictionary<string, CourseTheme>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, CourseTheme>(existingThemesByKey, StringComparer.OrdinalIgnoreCase);

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
                    if (!reusableThemes.TryGetValue(themeRequest.Key, out var theme))
                    {
                        theme = new CourseTheme
                        {
                            Id = Ulid.NewUlid(),
                            Branch = branch,
                            BranchId = branch.Id,
                            Key = themeRequest.Key,
                            Title = themeRequest.Title,
                            Description = themeRequest.Description,
                            LessonContent = themeRequest.LessonContent,
                            HomeworkContent = themeRequest.HomeworkContent,
                            Order = themeRequest.Order,
                            ExperiencePointsReward = themeRequest.ExperiencePointsReward
                        };
                    }
                    else
                    {
                        theme.Branch = branch;
                        theme.BranchId = branch.Id;
                        theme.Key = themeRequest.Key;
                        theme.Title = themeRequest.Title;
                        theme.Description = themeRequest.Description;
                        theme.LessonContent = themeRequest.LessonContent;
                        theme.HomeworkContent = themeRequest.HomeworkContent;
                        theme.Order = themeRequest.Order;
                        theme.ExperiencePointsReward = themeRequest.ExperiencePointsReward;
                        theme.Dependencies.Clear();
                    }

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

        foreach (var theme in themeByKey.Values)
        {
            if (!dependencyKeysByThemeId.TryGetValue(theme.Id, out var dependencyKeys))
            {
                continue;
            }

            foreach (var dependencyKey in dependencyKeys)
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
