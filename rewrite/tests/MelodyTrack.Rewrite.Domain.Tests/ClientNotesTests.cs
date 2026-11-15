using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ClientNotesTests
{
    [Fact]
    public async Task ChangeExpectedAsync_PersonalGrantForTeacher_AllowsChange()
    {
        var store = new MemoryStore("initial");
        var permission = new StaffPermissions(System.Collections.Immutable.ImmutableDictionary<StaffPermission, PermissionGrant>.Empty
            .Add(StaffPermission.ClientNotesWrite, new(true, PermissionDataScope.Available, PermissionTimeScope.Any)));
        var actor = new Actor.Staff(TestData.StaffId, StaffRole.Teacher, permission);

        var change = Ok(await new ClientNotesService(store).ChangeExpectedAsync(TestData.ClientId, Guid(70), "initial", "changed",
            Now, actor, TestContext.Current.CancellationToken));

        Assert.Equal("changed", store.Current);
        Assert.Equal(actor.Id, change.AuthorId);
    }

    [Fact]
    public async Task ChangeExpectedAsync_StaleClientState_DoesNotOverwriteCurrentNotes()
    {
        var store = new MemoryStore("current");
        var actor = new Actor.Staff(TestData.StaffId, StaffRole.Superuser);

        var result = await new ClientNotesService(store).ChangeExpectedAsync(TestData.ClientId, Guid(70), "stale", "changed",
            Now, actor, TestContext.Current.CancellationToken);

        AssertError(new ModelError.InvalidState(), result);
        Assert.Equal("current", store.Current);
        Assert.Empty(store.History);
    }

    [Fact]
    public async Task ChangeExpectedAsync_ReplayedAtLaterServerTime_ReturnsOriginalChange()
    {
        var store = new MemoryStore("initial");
        var service = new ClientNotesService(store);
        var actor = new Actor.Staff(TestData.StaffId, StaffRole.Superuser);
        var token = TestContext.Current.CancellationToken;
        var original = Ok(await service.ChangeExpectedAsync(TestData.ClientId, Guid(70), "initial", "first", Now, actor, token));
        Ok(await service.ChangeExpectedAsync(TestData.ClientId, Guid(71), "first", "second", Now.AddMinutes(1), actor, token));

        var replay = Ok(await service.ChangeExpectedAsync(TestData.ClientId, Guid(70), "initial", "first", Now.AddMinutes(2), actor, token));

        Assert.Equal(original, replay);
        Assert.Equal("second", store.Current);
        Assert.Equal(2, store.History.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  new text  ")]
    public async Task ChangeAsync_Reception_PreservesTextAuthorAndUtcTime(string? after)
    {
        var store = new MemoryStore("previous");
        var service = new ClientNotesService(store);
        var actor = new Actor.Staff(TestData.StaffId, StaffRole.Administrator);

        var change = Ok(await service.ChangeAsync(TestData.ClientId, Guid(70), after,
            Now.ToOffset(TimeSpan.FromHours(3)), actor, TestContext.Current.CancellationToken));

        Assert.Equal("previous", change.Before);
        Assert.Equal(after, change.After);
        Assert.Equal(after, store.Current);
        Assert.Equal(actor.Id, change.AuthorId);
        Assert.Equal(Now, change.ChangedAt);
        Assert.Equal(TimeSpan.Zero, change.ChangedAt.Offset);
        Assert.Equal(change, Assert.Single(store.History));
        Assert.DoesNotContain("previous", change.ToString());
    }

    [Fact]
    public async Task ChangeAsync_UnchangedText_DoesNotAppendHistory()
    {
        var store = new MemoryStore("same");

        var result = await new ClientNotesService(store).ChangeAsync(TestData.ClientId, Guid(70), "same", Now,
            new Actor.Staff(TestData.StaffId, StaffRole.Superuser), TestContext.Current.CancellationToken);

        AssertError(new ModelError.InvalidState(), result);
        Assert.Empty(store.History);
    }

    [Fact]
    public async Task ChangeAsync_ReplayedAfterLaterChange_ReturnsOriginalResultWithoutRewritingNotes()
    {
        var store = new MemoryStore("initial");
        var service = new ClientNotesService(store);
        var actor = new Actor.Staff(TestData.StaffId, StaffRole.Superuser);
        var token = TestContext.Current.CancellationToken;
        var now = Now.AddTicks(1);
        var first = Ok(await service.ChangeAsync(TestData.ClientId, Guid(70), "first", now, actor, token));
        Ok(await service.ChangeAsync(TestData.ClientId, Guid(71), "second", Now.AddMinutes(1), actor, token));

        var replayed = Ok(await service.ChangeAsync(TestData.ClientId, Guid(70), "first", now, actor, token));

        Assert.Equal(first, replayed);
        Assert.Equal("second", store.Current);
        Assert.Equal(2, store.History.Count);
    }

    [Fact]
    public async Task ChangeAsync_ReusedEntryIdWithDifferentText_RejectsCommand()
    {
        var store = new MemoryStore("initial");
        var service = new ClientNotesService(store);
        var actor = new Actor.Staff(TestData.StaffId, StaffRole.Superuser);
        var token = TestContext.Current.CancellationToken;
        Ok(await service.ChangeAsync(TestData.ClientId, Guid(70), "first", Now, actor, token));

        var result = await service.ChangeAsync(TestData.ClientId, Guid(70), "other", Now, actor, token);

        AssertError(new ModelError.InvalidState(), result);
        Assert.Equal("first", store.Current);
        Assert.Single(store.History);
    }

    [Fact]
    public async Task ChangeAsync_Teacher_DeniesBeforeReadingStore()
    {
        var store = new MemoryStore("previous");

        var result = await new ClientNotesService(store).ChangeAsync(TestData.ClientId, Guid(70), "new", Now,
            new Actor.Staff(TestData.StaffId, StaffRole.Teacher), TestContext.Current.CancellationToken);

        AssertError(new ModelError.Forbidden(), result);
        Assert.Equal(0, store.Reads);
        Assert.Empty(store.History);
    }

    [Fact]
    public async Task ReadHistoryAsync_Client_DeniesBeforeReadingStore()
    {
        var store = new MemoryStore("previous");
        var service = new ClientNotesService(store);
        var actor = new Actor.Client(TestData.ClientId);

        var history = await service.ReadHistoryAsync(TestData.ClientId, actor, TestContext.Current.CancellationToken);
        var current = await service.ReadCurrentAsync(TestData.ClientId, actor, TestContext.Current.CancellationToken);
        var change = await service.ChangeAsync(TestData.ClientId, Guid(70), "new", Now, actor, TestContext.Current.CancellationToken);

        AssertError(new ModelError.Forbidden(), history);
        AssertError(new ModelError.Forbidden(), current);
        AssertError(new ModelError.Forbidden(), change);
        Assert.Equal(0, store.Reads);
    }

    [Theory]
    [InlineData(StaffRole.Superuser)]
    [InlineData(StaffRole.Administrator)]
    [InlineData(StaffRole.Teacher)]
    public async Task ReadHistoryAsync_Staff_ReadsProtectedSubjectHistory(StaffRole role)
    {
        var store = new MemoryStore("new");
        var change = Ok(ClientNotesChange.Create(Guid(70), TestData.ClientId, TestData.StaffId, Now, "previous", "new"));
        store.History.Add(change);
        var actor = new Actor.Staff(TestData.StaffId, role);
        var service = new ClientNotesService(store);

        var result = Ok(await service.ReadHistoryAsync(TestData.ClientId, actor, TestContext.Current.CancellationToken));
        var current = Ok(await service.ReadCurrentAsync(TestData.ClientId, actor, TestContext.Current.CancellationToken));

        var entry = Assert.Single(result);
        Assert.Equal(change.Id, entry.Id);
        Assert.Equal("teacher", entry.AuthorName);
        Assert.Equal(change.ChangedAt, entry.ChangedAt);
        Assert.Equal(change.Before, entry.Before);
        Assert.Equal(change.After, entry.After);
        Assert.DoesNotContain("teacher", entry.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("previous", entry.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("new", entry.ToString(), StringComparison.Ordinal);
        Assert.Equal("new", current);
    }

    [Fact]
    public async Task ChangeAsync_Cancelled_DoesNotAccessStore()
    {
        var store = new MemoryStore("previous");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new ClientNotesService(store).ChangeAsync(
            TestData.ClientId, Guid(70), "new", Now, new Actor.Staff(TestData.StaffId, StaffRole.Superuser), cancellation.Token));

        Assert.Equal(0, store.Reads);
        Assert.Empty(store.History);
    }

    [Fact]
    public void UpdateProfile_ChangedNotes_RejectsHistoryBypass()
    {
        var client = MakeClient();

        var result = client.UpdateProfile(client.Profile with { Notes = "replacement" });

        AssertError(new ModelError.InvalidState(), result);
        Assert.Equal(Profile.Notes, client.Profile.Notes);
    }

    [Fact]
    public void UpdateProfile_UnchangedNotes_AllowsOtherProfileChanges()
    {
        var client = MakeClient();

        var updated = Ok(client.UpdateProfile(client.Profile with { Birthdate = new DateOnly(2000, 2, 29) }));

        Assert.Equal(new DateOnly(2000, 2, 29), updated.Profile.Birthdate);
        Assert.Equal(client.Profile.Notes, updated.Profile.Notes);
    }

    private sealed class MemoryStore(string? current) : IClientNotesStore
    {
        internal string? Current = current;
        internal int Reads;
        internal List<ClientNotesChange> History { get; } = [];

        public Task<Result<ClientNotesChange, ErrorList>> ChangeAsync(ClientNotesWriteCommand command, CancellationToken cancellationToken)
        {
            Reads++;
            var existing = History.SingleOrDefault(item => item.Id == command.EntryId);
            if (existing is not null)
                return Task.FromResult(existing.ClientId == command.ClientId && existing.AuthorId == command.AuthorId
                    && existing.After == command.Notes && (!command.HasExpectedState || existing.Before == command.ExpectedNotes)
                    ? Result.Success<ClientNotesChange, ErrorList>(existing)
                    : Result.Failure<ClientNotesChange, ErrorList>(ErrorList.Single(new ModelError.InvalidState(), "id")));
            if (command.HasExpectedState && Current != command.ExpectedNotes)
                return Task.FromResult(Result.Failure<ClientNotesChange, ErrorList>(ErrorList.Single(new ModelError.InvalidState(), "notes")));
            var change = ClientNotesChange.Create(command.EntryId, command.ClientId, command.AuthorId, command.ChangedAt, Current, command.Notes);
            return change.IsFailure ? Task.FromResult(change) : AppendAsync(change.Value, cancellationToken);
        }

        public Task<Result<string?, ErrorList>> GetCurrentAsync(ClientId clientId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Result.Success<string?, ErrorList>(Current));
        }

        public Task<Result<IReadOnlyList<ClientNotesHistoryEntry>, ErrorList>> GetHistoryAsync(ClientId clientId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Result.Success<IReadOnlyList<ClientNotesHistoryEntry>, ErrorList>(History.Select(item =>
                new ClientNotesHistoryEntry(item.Id, "teacher", item.ChangedAt, item.Before, item.After)).ToArray()));
        }

        public Task<Result<ClientNotesChange?, ErrorList>> FindChangeAsync(Guid entryId, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(Result.Success<ClientNotesChange?, ErrorList>(History.SingleOrDefault(change => change.Id == entryId)));
        }

        public Task<Result<ClientNotesChange, ErrorList>> AppendAsync(ClientNotesChange change, CancellationToken cancellationToken)
        {
            Current = change.After;
            History.Add(change);
            return Task.FromResult(Result.Success<ClientNotesChange, ErrorList>(change));
        }
    }
}
