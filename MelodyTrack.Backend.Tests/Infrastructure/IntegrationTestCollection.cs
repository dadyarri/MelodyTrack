namespace MelodyTrack.Backend.Tests.Infrastructure;

[CollectionDefinition(Name, DisableParallelization = true)]
[Trait("Category", "Integration")]
public sealed class IntegrationTestCollection : ICollectionFixture<MelodyTrackFixture>
{
    public const string Name = "backend-integration";
}
