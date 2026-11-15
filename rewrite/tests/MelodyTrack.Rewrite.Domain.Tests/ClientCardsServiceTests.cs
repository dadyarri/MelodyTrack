using System.Collections.Immutable;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using Xunit;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ClientCardsServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private static readonly ClientId Client = new(Guid.Parse("019a4000-0000-7000-8000-000000000001"));
    private static readonly StaffId Staff = new(Guid.Parse("019a4000-0000-7000-8000-000000000002"));
    private static readonly Guid Source = Guid.Parse("019a4000-0000-7000-8000-000000000003");
    private static ClientCardValues Values => new("Synthetic", "Example", null, null, Source, []);

    private static Actor.Staff Actor(PermissionGrant grant, StaffRole role = StaffRole.Teacher) =>
        new(Staff, role, new(ImmutableDictionary<StaffPermission, PermissionGrant>.Empty
            .Add(StaffPermission.ClientsWrite, grant)));

    private static Actor.Staff Writer => Actor(new(true, PermissionDataScope.Available, PermissionTimeScope.Any));

    [Theory]
    [InlineData(false, PermissionDataScope.Available)]
    [InlineData(true, PermissionDataScope.Own)]
    public async Task Create_UnsupportedGrant_DeniesBeforePersistence(bool allowed, PermissionDataScope scope)
    {
        var store = new Store();

        var result = await new ClientCardsService(store).CreateAsync(Guid.NewGuid(), Staff, Client, Values,
            Actor(new(allowed, scope, PermissionTimeScope.Any)), Now, Token);

        Assert.IsType<OperationError.Forbidden>(result.Error);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Create_GrantedTeacherWithEmptyContacts_SavesWithoutRoleBypass()
    {
        var store = new Store();

        var result = await new ClientCardsService(store).CreateAsync(Guid.NewGuid(), Staff, Client,
            Values, Writer, Now, Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.Writes);
        Assert.Equal(Staff, store.Author);
        Assert.Empty(store.Values!.Contacts);
    }

    [Fact]
    public async Task Create_MissingSource_RejectsBeforePersistence()
    {
        var store = new Store();

        var result = await new ClientCardsService(store).CreateAsync(Guid.NewGuid(), Staff, Client,
            Values with { SourceId = null }, Writer, Now, Token);

        var error = Assert.IsType<OperationError.Validation>(result.Error);
        Assert.Contains(error.Errors.Issues, issue => issue.Field == "sourceId");
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Create_ImpersonatedAuthor_DeniesBeforePersistence()
    {
        var store = new Store();

        var result = await new ClientCardsService(store).CreateAsync(Guid.NewGuid(), new(Guid.NewGuid()),
            Client, Values, Writer, Now, Token);

        Assert.IsType<OperationError.Forbidden>(result.Error);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Update_MissingExpectedState_RejectsBeforePersistence()
    {
        var store = new Store();

        var result = await new ClientCardsService(store).UpdateAsync(Guid.NewGuid(), Staff, Client,
            null, Values, Writer, Now, Token);

        Assert.IsType<OperationError.Validation>(result.Error);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Update_ConcurrentChange_ReturnsConflictWithoutExposingValues()
    {
        var store = new Store { UpdateConflict = true };
        var expected = new ClientCardExpectedState(Values with { FirstName = "PrivateOriginal" });

        var result = await new ClientCardsService(store).UpdateAsync(Guid.NewGuid(), Staff, Client,
            expected, Values with { FirstName = "PrivateReplacement" }, Writer, Now, Token);

        var error = Assert.IsType<OperationError.Conflict>(result.Error);
        Assert.Same(expected, store.Expected);
        Assert.DoesNotContain("Private", error.ToString());
    }

    [Fact]
    public async Task Update_LegacyExpectedStateWithoutSource_AllowsSelectingSource()
    {
        var store = new Store();
        var expected = new ClientCardExpectedState(Values with { SourceId = null });

        var result = await new ClientCardsService(store).UpdateAsync(Guid.NewGuid(), Staff, Client,
            expected, Values, Writer, Now, Token);

        Assert.True(result.IsSuccess);
        Assert.Same(expected, store.Expected);
        Assert.Equal(Source, store.Values!.SourceId);
    }

    [Fact]
    public void Archive_GrantedWriter_ReportsUnavailableDependencies()
    {
        var store = new Store();

        var result = new ClientCardsService(store).Archive(Writer, Now);

        var error = Assert.IsType<OperationError.Conflict>(result.Error);
        Assert.Contains(error.Errors.Issues,
            issue => issue.Error is ClientError.ArchiveDependenciesNotMigrated);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Restore_ImpersonatedAuthor_DeniesBeforePersistence()
    {
        var store = new Store();

        var result = await new ClientCardsService(store).RestoreAsync(Guid.NewGuid(), new(Guid.NewGuid()),
            Client, Writer, Now, Token);

        Assert.IsType<OperationError.Forbidden>(result.Error);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Create_CancelledRequest_DoesNotWrite()
    {
        var store = new Store();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await new ClientCardsService(store).CreateAsync(Guid.NewGuid(), Staff, Client, Values,
                Writer, Now, cancellation.Token));

        Assert.Equal(0, store.Writes);
    }

    private sealed class Store : IClientCardsStore
    {
        public int Writes { get; private set; }
        public StaffId Author { get; private set; }
        public ClientCardValues? Values { get; private set; }
        public ClientCardExpectedState? Expected { get; private set; }
        public bool UpdateConflict { get; init; }

        public Task<Result<ClientCard, ErrorList>> ReadAsync(ClientId clientId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Failure<ClientCard, ErrorList>(ErrorList.Single(new ModelError.NotFound())));

        public Task<Result<ClientCardCommandResult, ErrorList>> CreateAsync(Guid commandId, StaffId authorId,
            ClientId clientId, ClientCardValues values, byte[] requestFingerprint, DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Writes++;
            Author = authorId;
            Values = values;
            return Task.FromResult(Result.Success<ClientCardCommandResult, ErrorList>(new(null, false)));
        }

        public Task<Result<ClientCardCommandResult, ErrorList>> UpdateAsync(Guid commandId, StaffId authorId,
            ClientId clientId, ClientCardExpectedState expectedState, ClientCardValues values,
            byte[] requestFingerprint, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Writes++;
            Expected = expectedState;
            Values = values;
            return Task.FromResult(UpdateConflict
                ? Result.Failure<ClientCardCommandResult, ErrorList>(ErrorList.Single(new ModelError.InvalidState()))
                : Result.Success<ClientCardCommandResult, ErrorList>(new(null, false)));
        }

        public Task<UnitResult<ErrorList>> RestoreAsync(Guid commandId, StaffId authorId, ClientId clientId,
            byte[] requestFingerprint, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Writes++;
            Author = authorId;
            return Task.FromResult(UnitResult.Success<ErrorList>());
        }
    }
}
