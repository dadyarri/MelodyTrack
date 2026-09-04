using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Data.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public required string ConnectionString { get; init; }

    public bool EnableSensitiveDataLogging { get; init; }
}
