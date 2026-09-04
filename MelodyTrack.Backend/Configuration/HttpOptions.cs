namespace MelodyTrack.Backend.Configuration;

public sealed class HttpOptions
{
    public const string SectionName = "Http";

    public string PathBase { get; init; } = string.Empty;
}
