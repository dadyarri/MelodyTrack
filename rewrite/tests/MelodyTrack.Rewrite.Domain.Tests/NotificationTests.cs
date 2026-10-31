using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class NotificationTests
{
    [Fact]
    public void CanDeliver_InactiveRememberedAccount_ReceivesNoPush()
    {
        var recipient = new Actor.Client(TestData.ClientId);
        var notification = Ok(NotificationRules.Create(new(Guid(100)), recipient, new NotificationEvent.CourseAssigned(new(Guid(101))), Utc(1, 0)));
        var preferences = new DevicePushPreference(new Actor.Client(new(Guid(102))), true);

        Assert.False(preferences.CanDeliver(notification, true));
    }

    [Fact]
    public void CanDeliver_InvalidSession_ReceivesNoPush()
    {
        var recipient = new Actor.Client(TestData.ClientId);
        var notification = Ok(NotificationRules.Create(new(Guid(100)), recipient, new NotificationEvent.CourseAssigned(new(Guid(101))), Utc(1, 0)));

        Assert.False(new DevicePushPreference(recipient, true).CanDeliver(notification, false));
    }
}
