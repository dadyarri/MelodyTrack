using System.Collections.Immutable;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class AnnouncementTests
{
    [Fact]
    public void IsVisibleTo_CombinedAudiences_PreservesPublicAndSelectedRecipients()
    {
        var audience = new AnnouncementAudience(false, true, true, [TestData.StaffId], []);
        var announcement = Ok(AnnouncementRules.Create(new(Guid(200)), LearningDocument.Empty, audience, Utc(3, 0), true));

        Assert.True(announcement.IsVisibleTo(null, Now, false));
        Assert.True(announcement.IsVisibleTo(new Actor.Client(TestData.ClientId), Now, false));
        Assert.True(announcement.IsVisibleTo(new Actor.Staff(TestData.StaffId, StaffRole.Teacher), Now, false));
        Assert.False(announcement.IsVisibleTo(new Actor.Staff(new(Guid(201)), StaffRole.Superuser), Now, false));
    }

    [Fact]
    public void IsVisibleTo_AtEndTime_HidesAnnouncement()
    {
        var announcement = Ok(AnnouncementRules.Create(new(Guid(200)), LearningDocument.Empty,
            new(true, false, true, [], []), Utc(3, 0), false));

        Assert.True(announcement.IsVisibleTo(null, Utc(3, 0).AddTicks(-1), false));
        Assert.False(announcement.IsVisibleTo(null, Utc(3, 0), false));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void IsVisibleTo_Dismissed_UsesAnnouncementSetting(bool canDismiss, bool expectedVisible)
    {
        var announcement = Ok(AnnouncementRules.Create(new(Guid(200)), LearningDocument.Empty,
            new(false, false, true, [], []), Utc(3, 0), canDismiss));

        Assert.Equal(expectedVisible, announcement.IsVisibleTo(null, Now, true));
    }

    [Fact]
    public void Create_EmptyAudience_RejectsAnnouncement()
    {
        var result = AnnouncementRules.Create(new(Guid(200)), LearningDocument.Empty,
            new(false, false, false, [], []), Utc(3, 0), true);

        AssertError(new ModelError.InvalidValue(), result);
    }
}
