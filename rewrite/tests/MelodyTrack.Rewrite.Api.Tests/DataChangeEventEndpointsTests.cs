using System.Collections.Immutable;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using MelodyTrack.Rewrite.Api.Tests;
using MelodyTrack.Rewrite.Domain;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class DataChangeEventEndpointsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stream_AuthenticatedStaffReceivesInitialAndSuccessfulMutationInvalidations()
    {
        await using var host = await StaffApiHost.StartAsync(Teacher(), Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/changes/events");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);
        using var stream = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Token);

        Assert.Equal(HttpStatusCode.OK, stream.StatusCode);
        Assert.Equal("text/event-stream", stream.Content.Headers.ContentType?.MediaType);
        await using var body = await stream.Content.ReadAsStreamAsync(Token);
        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);
        var initial = await ReadEventBlockAsync(reader, Token);
        Assert.Contains("event: invalidate", initial);
        Assert.Contains("data: 0", initial);

        using var mutation = new HttpRequestMessage(HttpMethod.Post, "/api/_test/mutation");
        mutation.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);
        using var changed = await host.Client.SendAsync(mutation, Token);
        Assert.Equal(HttpStatusCode.NoContent, changed.StatusCode);

        var update = await ReadEventBlockAsync(reader, Token);
        Assert.Contains("event: invalidate", update);
        Assert.Contains("data: 1", update);

        using var reconnectRequest = new HttpRequestMessage(HttpMethod.Get, "/api/changes/events");
        reconnectRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);
        using var reconnect = await host.Client.SendAsync(reconnectRequest, HttpCompletionOption.ResponseHeadersRead, Token);
        await using var reconnectBody = await reconnect.Content.ReadAsStreamAsync(Token);
        using var reconnectReader = new StreamReader(reconnectBody, Encoding.UTF8, leaveOpen: true);

        var catchUp = await ReadEventBlockAsync(reconnectReader, Token);
        Assert.Contains("event: invalidate", catchUp);
        Assert.Contains("data: 1", catchUp);
    }

    [Fact]
    public async Task Stream_AnonymousRequest_IsRejectedBeforeOpeningStream()
    {
        await using var host = await StaffApiHost.StartAsync(Teacher(), Token);

        using var response = await host.Client.GetAsync("/api/changes/events", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual("text/event-stream", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Stream_AuthenticatedClientReceivesInitialInvalidation()
    {
        await using var host = await StaffApiHost.StartAsync(Teacher(), Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/changes/events");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.ClientToken);
        using var response = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Token);
        await using var body = await response.Content.ReadAsStreamAsync(Token);
        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("event: invalidate", await ReadEventBlockAsync(reader, Token));
    }

    [Fact]
    public async Task Stream_RevokedStaffSession_ClosesAfterRevalidation()
    {
        await using var host = await StaffApiHost.StartAsync(Teacher(), Token);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/changes/events");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);
        using var stream = await host.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, Token);
        await using var body = await stream.Content.ReadAsStreamAsync(Token);
        using var reader = new StreamReader(body, Encoding.UTF8, leaveOpen: true);
        Assert.Contains("event: invalidate", await ReadEventBlockAsync(reader, Token));

        host.Tokens.Revoked = true;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        string? line;
        do
        {
            line = await reader.ReadLineAsync(timeout.Token);
        } while (line is not null);

        Assert.Null(line);
    }

    private static async Task<string[]> ReadEventBlockAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var lines = new List<string>();
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Length == 0 && lines.Count > 0) return [.. lines];
            if (line.Length > 0 && !line.StartsWith(':')) lines.Add(line);
        }
        return [.. lines];
    }

    private static Actor.Staff Teacher() => new(StaffApiHost.Staff, StaffRole.Teacher,
        new(ImmutableDictionary<StaffPermission, PermissionGrant>.Empty));
}
