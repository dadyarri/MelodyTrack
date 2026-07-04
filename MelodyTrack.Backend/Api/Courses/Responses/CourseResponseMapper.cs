using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Courses.Responses;

public static class CourseResponseMapper
{
    public static CourseDto MapCourse(Course course)
    {
        return new CourseDto
        {
            Id = course.Id,
            Name = course.Name,
            Description = course.Description,
            CreatedAtUtc = course.CreatedAtUtc,
            UpdatedAtUtc = course.UpdatedAtUtc,
            Levels = course.Levels
                .OrderBy(level => level.Order)
                .Select(level => new CourseLevelDto
                {
                    Id = level.Id,
                    Title = level.Title,
                    Order = level.Order,
                    RequiredExperiencePoints = level.RequiredExperiencePoints
                })
                .ToList(),
            Blocks = course.Blocks
                .OrderBy(block => block.Order)
                .Select(block => new CourseBlockDto
                {
                    Id = block.Id,
                    Title = block.Title,
                    Description = block.Description,
                    Order = block.Order,
                    Branches = block.Branches
                        .OrderBy(branch => branch.Order)
                        .Select(branch => new CourseBranchDto
                        {
                            Id = branch.Id,
                            Title = branch.Title,
                            Description = branch.Description,
                            Order = branch.Order,
                            Themes = branch.Themes
                                .OrderBy(theme => theme.Order)
                                .Select(theme => new CourseThemeDto
                                {
                                    Id = theme.Id,
                                    Key = theme.Key,
                                    Title = theme.Title,
                                    Description = theme.Description,
                                    LessonContent = theme.LessonContent,
                                    HomeworkContent = theme.HomeworkContent,
                                    Order = theme.Order,
                                    ExperiencePointsReward = theme.ExperiencePointsReward,
                                    DependencyThemeIds = theme.Dependencies
                                        .OrderBy(dependency => dependency.DependsOnThemeId)
                                        .Select(dependency => dependency.DependsOnThemeId)
                                        .ToList()
                                })
                                .ToList()
                        })
                        .ToList()
                })
                .ToList()
        };
    }
}
