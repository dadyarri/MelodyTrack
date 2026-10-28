using MelodyTrack.Rewrite.Domain.Scheduling;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class RoomTests
{
    [Fact]
    public void Create_BlankName_RejectsRoom() => AssertError(new CatalogueError.Required("name"), Room.Create(TestData.RoomId, " ", null));
    [Fact]
    public void Archive_UsedRoom_PreservesIdentityAndComment()
    {
        var room = Ok(Room.Create(TestData.RoomId, " Зал ", " пианино ")).Archive();
        Assert.Equal(TestData.RoomId, room.Id);
        Assert.Equal("Зал", room.Name);
        Assert.Equal("пианино", room.Comment);
        Assert.True(room.IsArchived);
        Assert.False((NoUsage with { HasHistoricalUse = true }).CanDelete);
    }
    [Fact]
    public void CanDelete_NeverUsedRoom_ReturnsTrue()
    {
        Assert.True(NoUsage.CanDelete);
        Assert.False((NoUsage with { HasActiveSeries = true }).CanDelete);
        Assert.False((NoUsage with { HasFutureAppointments = true }).CanDelete);
    }
}
