using MelodyTrack.Rewrite.Application;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class YouTubeClipTests
{
    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123&t=100s", 100)]
    [InlineData("https://youtu.be/abc123?t=1m40s", 100)]
    [InlineData("https://www.youtube.com/watch?v=abc123&start=100", 100)]
    public void FromUrl_StartParameter_PreservesSeconds(string url, int seconds)
    {
        var clip = Ok(YouTubeClip.FromUrl(url));

        Assert.Equal("abc123", clip.VideoId);
        Assert.Equal(seconds, clip.StartsAtSeconds);
    }

    [Fact]
    public void Create_EndBeforeStart_RejectsClip()
    {
        AssertError(new ModelError.InvalidValue(), YouTubeClip.Create("abc123", 100, 50));
    }
}
