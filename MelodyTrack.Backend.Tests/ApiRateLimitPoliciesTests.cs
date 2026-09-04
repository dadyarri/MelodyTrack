using System.Net;
using MelodyTrack.Backend.ErrorHandling;
using Microsoft.AspNetCore.Http;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class ApiRateLimitPoliciesTests
{
    [Fact]
    public void ClientAddressPartitionKey_IgnoresUnprocessedForwardedHeader()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("198.51.100.25");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10, 192.0.2.42";

        ApiRateLimitPolicies.GetClientAddressPartitionKey(context).ShouldBe("198.51.100.25");
    }

    [Fact]
    public void ClientAddressPartitionKey_UsesMiddlewareNormalizedConnectionAddress()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.42");
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.10";

        ApiRateLimitPolicies.GetClientAddressPartitionKey(context).ShouldBe("192.0.2.42");
    }
}
