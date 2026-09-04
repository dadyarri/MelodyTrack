using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Core.Configuration;

public sealed class WebPushOptions
{
    public const string SectionName = "WebPush";

    public bool Enabled { get; init; }

    [MaxLength(512)]
    public string Subject { get; init; } = string.Empty;

    [MaxLength(512)]
    public string PublicKey { get; init; } = string.Empty;

    [MaxLength(512)]
    public string PrivateKey { get; init; } = string.Empty;
}
