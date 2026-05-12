namespace MelodyTrack.Backend.Tests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class IntegrationTestCollection : ICollectionFixture<MelodyTrackFixture>
{
    public const string Name = "backend-integration";
}
