using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Core.Configuration;

public sealed class PublicUrlOptions
{
    public const string SectionName = "PublicUrl";

    [Required]
    public required string BaseUrl { get; init; }
}
