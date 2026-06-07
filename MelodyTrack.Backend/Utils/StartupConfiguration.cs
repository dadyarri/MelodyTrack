namespace MelodyTrack.Backend.Utils;

public sealed class StartupConfiguration
{
    public required string Environment { get; init; }
    public required string AppDomain { get; init; }
    public required string JwtSigningKey { get; init; }
    public required string PiiMasterKeyVersion { get; init; }
    public required string PiiMasterKey { get; init; }
    public required IReadOnlyDictionary<string, string> PiiMasterKeys { get; init; }
    public required string DatabaseUrl { get; init; }
    public required string QuartzSqlPath { get; init; }
}
