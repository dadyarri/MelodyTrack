using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace MelodyTrack.Backend.Data.Models;

/// <summary>A durable trainer definition owned by one course theme.</summary>
public class CourseThemePracticeExercise : BaseModel
{
    public required Ulid CourseThemeId { get; set; }

    [JsonIgnore]
    public CourseTheme CourseTheme { get; set; } = null!;

    [MaxLength(100)]
    public required string Type { get; set; }

    public required string Configuration { get; set; }
}
