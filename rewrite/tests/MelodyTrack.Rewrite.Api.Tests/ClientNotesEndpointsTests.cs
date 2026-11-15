using System.Collections.Immutable;
using System.Net;
using MelodyTrack.Rewrite.Api;
using MelodyTrack.Rewrite.Domain;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class ClientNotesEndpointsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly Guid Client = Guid.Parse("019a2c63-8c00-7000-8000-000000000002");
    private static readonly Guid Command = Guid.Parse("019a2c63-8c00-7000-8000-000000000003");
    private static string Path => $"/api/clients/{Client}/notes";

    [Fact]
    public async Task Put_GrantedTeacher_ChangesNotesWithoutRoleBypass()
    {
        var store = new StaffApiHost.NotesState("initial");
        await using var host = await StaffApiHost.StartAsync(GrantedTeacher(), Token, notes: store);

        using var result = await host.SendAsync(HttpMethod.Put, Path,
            new ClientNotesPutRequest(Command, "next", new("initial")), Token);

        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        Assert.Equal("next", store.Current);
        Assert.Equal(StaffApiHost.Staff, Assert.Single(store.History).AuthorId);
    }

    [Fact]
    public async Task Put_PermissionRevoked_DeniesReplayBeforeReadingNotes()
    {
        var store = new StaffApiHost.NotesState("initial");
        await using var host = await StaffApiHost.StartAsync(GrantedTeacher(), Token, notes: store);
        var command = new ClientNotesPutRequest(Command, "next", new("initial"));
        using var first = await host.SendAsync(HttpMethod.Put, Path, command, Token);
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        host.Access.Actor = new(StaffApiHost.Staff, StaffRole.Teacher, new(ImmutableDictionary<StaffPermission, PermissionGrant>.Empty));
        var reads = store.Reads;

        using var result = await host.SendAsync(HttpMethod.Put, Path, command, Token);

        Assert.Equal(HttpStatusCode.Forbidden, result.StatusCode);
        Assert.Equal(reads, store.Reads);
        Assert.Single(store.History);
    }

    [Fact]
    public async Task Put_StaleExpectedText_ReturnsConflictWithoutExposingText()
    {
        var store = new StaffApiHost.NotesState("private current");
        await using var host = await StaffApiHost.StartAsync(GrantedTeacher(), Token, notes: store);

        using var result = await host.SendAsync(HttpMethod.Put, Path,
            new ClientNotesPutRequest(Command, "private replacement", new("private stale")), Token);

        Assert.Equal(HttpStatusCode.Conflict, result.StatusCode);
        Assert.DoesNotContain("private", await result.Content.ReadAsStringAsync(Token));
        Assert.Empty(store.History);
    }

    [Fact]
    public async Task Put_IdenticalCommandAfterLaterChange_ReturnsSuccessWithoutRestoringOldNotes()
    {
        var store = new StaffApiHost.NotesState("initial");
        await using var host = await StaffApiHost.StartAsync(GrantedTeacher(), Token, notes: store);
        var first = new ClientNotesPutRequest(Command, "first", new("initial"));
        using var initial = await host.SendAsync(HttpMethod.Put, Path, first, Token);
        Assert.Equal(HttpStatusCode.NoContent, initial.StatusCode);
        host.Time.Now = host.Time.Now.AddMinutes(1);
        using var later = await host.SendAsync(HttpMethod.Put, Path,
            new ClientNotesPutRequest(Guid.NewGuid(), "second", new("first")), Token);
        Assert.Equal(HttpStatusCode.NoContent, later.StatusCode);
        host.Time.Now = host.Time.Now.AddMinutes(1);

        using var result = await host.SendAsync(HttpMethod.Put, Path, first, Token);

        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        Assert.Equal("second", store.Current);
        Assert.Equal(2, store.History.Count);
    }

    [Fact]
    public async Task Put_MissingExpectedState_RejectsBeforeReadingNotes()
    {
        var store = new StaffApiHost.NotesState(null);
        await using var host = await StaffApiHost.StartAsync(GrantedTeacher(), Token, notes: store);

        using var result = await host.SendAsync(HttpMethod.Put, Path, new ClientNotesPutRequest(Command, "next", null), Token);

        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode);
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task GetCurrent_GrantedReader_ReturnsNoStoreNoteValue()
    {
        var store = new StaffApiHost.NotesState("private note");
        await using var host = await StaffApiHost.StartAsync(GrantedReader(), Token, notes: store);
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);

        using var response = await host.Client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("no-store", response.Headers.CacheControl?.ToString());
        Assert.Contains("private note", await response.Content.ReadAsStringAsync(Token));
    }

    [Fact]
    public async Task GetHistory_GrantedReader_ReturnsAuthorAndChanges()
    {
        var store = new StaffApiHost.NotesState("current");
        var change = ClientNotesChange.Create(Command, new(Client), StaffApiHost.Staff,
            new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero), "before", "current").Value;
        store.History.Add(change);
        await using var host = await StaffApiHost.StartAsync(GrantedReader(), Token, notes: store);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Path}/history");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);

        using var response = await host.Client.SendAsync(request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("authorName", await response.Content.ReadAsStringAsync(Token));
        Assert.Contains("teacher", await response.Content.ReadAsStringAsync(Token));
    }

    private static Actor.Staff GrantedTeacher() => new(StaffApiHost.Staff, StaffRole.Teacher,
        new(ImmutableDictionary<StaffPermission, PermissionGrant>.Empty.Add(StaffPermission.ClientNotesWrite,
            new(true, PermissionDataScope.Available, PermissionTimeScope.Any))));

    private static Actor.Staff GrantedReader() => new(StaffApiHost.Staff, StaffRole.Teacher,
        new(ImmutableDictionary<StaffPermission, PermissionGrant>.Empty.Add(StaffPermission.ClientNotesRead,
            new(true, PermissionDataScope.Available, PermissionTimeScope.Any))));
}
