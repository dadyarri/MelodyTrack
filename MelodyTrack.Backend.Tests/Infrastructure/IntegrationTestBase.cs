namespace MelodyTrack.Backend.Tests.Infrastructure;

public abstract class IntegrationTestBase(MelodyTrackFixture app) : IAsyncLifetime
{
    protected MelodyTrackFixture App { get; } = app;

    public virtual async ValueTask InitializeAsync()
    {
        await App.ResetStateAsync(TestContext.Current.CancellationToken);
    }

    public virtual ValueTask DisposeAsync()
    {
        App.Client.DefaultRequestHeaders.Clear();
        return ValueTask.CompletedTask;
    }
}
