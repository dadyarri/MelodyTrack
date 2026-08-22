namespace MelodyTrack.Backend.Tests;

public sealed class EmptyRequest
{
    public static EmptyRequest Instance { get; } = new();

    private EmptyRequest()
    {
    }
}
