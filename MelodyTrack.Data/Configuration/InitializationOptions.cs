namespace MelodyTrack.Data.Configuration;

public sealed class InitializationOptions
{
    public const string SectionName = "Initialization";

    public bool LogBootstrapSecrets { get; init; }
    public string QuartzSqlPath { get; init; } = "quartz.sql";
}
