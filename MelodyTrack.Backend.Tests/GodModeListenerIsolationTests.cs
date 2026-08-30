using MelodyTrack.Backend.GodMode;
using MelodyTrack.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class GodModeListenerIsolationTests
{
    [Theory]
    [InlineData(8080, "/god-mode/", false)]
    [InlineData(8081, "/api/auth/me", false)]
    [InlineData(8081, "/god-mode/", true)]
    [InlineData(8080, "/api/auth/me", true)]
    public async Task ListenerIsolation_RequestSurface_AllowsOnlyMatchingListener(
        int localPort,
        string path,
        bool expectedNextCalled)
    {
        var nextCalled = false;
        using var services = new ServiceCollection().BuildServiceProvider();
        var builder = new ApplicationBuilder(services);
        builder.UseGodModeListenerIsolation(new GodModeOptions
        {
            Port = 8081
        });
        builder.Run(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });
        var pipeline = builder.Build();
        var context = new DefaultHttpContext
        {
            RequestServices = services
        };
        context.Connection.LocalPort = localPort;
        context.Request.Path = path;

        await pipeline(context);

        nextCalled.ShouldBe(expectedNextCalled);
        if (!expectedNextCalled)
        {
            context.Response.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        }
    }
}
