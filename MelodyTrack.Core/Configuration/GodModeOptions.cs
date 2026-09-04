using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Core.Configuration;

public sealed class GodModeOptions
{
    public const string SectionName = "GodMode";

    [Range(1, 65535)]
    public int Port { get; init; } = 8081;

    [Required]
    public string StateDirectory { get; init; } = "/var/lib/melodytrack/god-mode";

    [Required]
    public string PublicBaseUrl { get; init; } = string.Empty;

    [Required]
    public string SessionSigningKey { get; init; } = string.Empty;

    [Range(1, 15)]
    public int BootstrapTokenLifetimeMinutes { get; init; } = 5;

    [Range(5, 60)]
    public int SessionLifetimeMinutes { get; init; } = 30;
}
