namespace MelodyTrack.Backend.Data.Models;

public class CourseThemeDependency : BaseModel
{
    public required Ulid ThemeId { get; set; }

    public required CourseTheme Theme { get; set; }

    public required Ulid DependsOnThemeId { get; set; }

    public required CourseTheme DependsOnTheme { get; set; }
}
