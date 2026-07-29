using System.Net;
using MelodyTrack.Backend.ErrorHandling;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class ApiRateLimitPoliciesTests
{
    [Fact]
    public void ClientAddressPartitionKey_UsesProxyAppendedAddressInsteadOfSpoofedPrefix()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.25");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10, 192.0.2.42";

        ApiRateLimitPolicies.GetClientAddressPartitionKey(context).ShouldBe("192.0.2.42");
    }

    [Fact]
    public void ClientAddressPartitionKey_FallsBackToConnectionForInvalidForwardedAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.25");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10, unknown";

        ApiRateLimitPolicies.GetClientAddressPartitionKey(context).ShouldBe("198.51.100.25");
    }
}
