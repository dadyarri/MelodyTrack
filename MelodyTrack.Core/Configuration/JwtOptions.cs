using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Core.Configuration;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public required string Issuer { get; init; }

    [Required]
    public required string Audience { get; init; }

    [Range(1, 15)]
    public int AccessTokenLifetimeMinutes { get; init; } = 10;
}
