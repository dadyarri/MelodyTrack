namespace MelodyTrack.Backend.Configuration;

public sealed class ReverseProxyOptions
{
    public const string SectionName = "ReverseProxy";

    public string[] KnownProxies { get; init; } = [];
    public string[] KnownNetworks { get; init; } = [];
}
