using System.Collections.Immutable;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using Xunit;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ClientDirectoryServiceTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static Actor.Staff Actor(PermissionGrant grant) => new(new(Guid.NewGuid()), StaffRole.Administrator,
        new(ImmutableDictionary<StaffPermission, PermissionGrant>.Empty.Add(StaffPermission.ClientsRead, grant)));

    [Theory]
    [InlineData(false, PermissionDataScope.Available, PermissionTimeScope.Any)]
    [InlineData(true, PermissionDataScope.Own, PermissionTimeScope.Any)]
    [InlineData(true, PermissionDataScope.Available, PermissionTimeScope.Today)]
    public async Task Read_UnsupportedGrant_DeniesBeforeReadingStore(bool allowed, PermissionDataScope scope, PermissionTimeScope time)
    {
        var store = new Store();
        var service = new ClientDirectoryService(store);
        var actor = Actor(new(allowed, scope, time));

        var page = await service.ReadAsync(new(), actor, Token);
        var options = await service.ReadOptionsAsync(actor, Token);

        Assert.IsType<OperationError.Forbidden>(page.Error);
        Assert.IsType<OperationError.Forbidden>(options.Error);
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task Read_AllowedGrant_TrimsSearchAndPreservesFilters()
    {
        var store = new Store();
        var service = new ClientDirectoryService(store);
        var query = new ClientDirectoryQuery(Q: "  Synthetic  ", Archive: "all", Page: 2, PageSize: 50, Sort: "createdAt:desc,name:asc");

        var result = await service.ReadAsync(query, Actor(new(true, PermissionDataScope.Available, PermissionTimeScope.Any)), Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(query with { Q = "Synthetic" }, store.LastQuery);
        Assert.Equal(1, store.Reads);
    }

    [Fact]
    public async Task Read_InvalidConflictingFilters_DoesNotReadStoreOrExposeQuery()
    {
        var store = new Store();
        var query = new ClientDirectoryQuery(Q: "private synthetic value", SourceId: Guid.NewGuid(), WithoutSource: true,
            Sort: "name:asc,name:desc", Page: 0);

        var result = await new ClientDirectoryService(store).ReadAsync(query,
            Actor(new(true, PermissionDataScope.Available, PermissionTimeScope.Any)), Token);

        var error = Assert.IsType<OperationError.Validation>(result.Error);
        Assert.Equal(["sourceId", "sort", "page"], error.Errors.Issues.Select(issue => issue.Field));
        Assert.Equal(0, store.Reads);
        Assert.DoesNotContain("private synthetic value", query.ToString());
    }

    [Theory]
    [InlineData("invalid", "name:asc", 10)]
    [InlineData("active", "unknown:asc", 10)]
    [InlineData("active", "name:unknown", 10)]
    [InlineData("active", "name:asc", 101)]
    [InlineData("active", "name", 10)]
    [InlineData("active", "name:asc,", 10)]
    [InlineData("active", "name:asc,name:desc", 10)]
    [InlineData("active", "name:asc,createdAt:desc,birthdate:asc,source:asc,status:asc,name:asc", 10)]
    [InlineData("active", "balance:asc", 10)]
    public void Validate_InvalidQuery_RejectsValues(string archive, string sort, int pageSize)
    {
        var query = new ClientDirectoryQuery(Archive: archive, Sort: sort, PageSize: pageSize);

        Assert.True(query.Validate().IsFailure);
    }

    [Theory]
    [InlineData(200, true)]
    [InlineData(201, false)]
    public async Task Read_QueryLength_EnforcesInclusiveLimit(int length, bool accepted)
    {
        var store = new Store();
        var query = new ClientDirectoryQuery(Q: new string('x', length));

        var result = await new ClientDirectoryService(store).ReadAsync(query,
            Actor(new(true, PermissionDataScope.Available, PermissionTimeScope.Any)), Token);

        Assert.Equal(accepted, result.IsSuccess);
        Assert.Equal(accepted ? 1 : 0, store.Reads);
        if (!accepted) Assert.IsType<OperationError.Validation>(result.Error);
    }

    [Fact]
    public void ReadSorts_MultipleColumns_PreservesPriorityAndDirection()
    {
        var query = new ClientDirectoryQuery(Sort: "source:asc,createdAt:desc,birthdate:asc,name:desc,status:asc");

        var sorts = query.ReadSorts();

        Assert.Equal(["source", "createdAt", "birthdate", "name", "status"], sorts.Value.Select(item => item.Key));
        Assert.Equal([false, true, false, true, false], sorts.Value.Select(item => item.Descending));
    }

    [Fact]
    public void ReadSorts_OmittedSelection_UsesStableNameOrderAndTenRowPage()
    {
        var query = new ClientDirectoryQuery();

        Assert.Equal(10, query.PageSize);
        Assert.Equal(new ClientDirectorySort("name", false), Assert.Single(query.ReadSorts().Value));
    }

    [Fact]
    public void ReadSorts_ExplicitEmptySelection_LeavesOnlyIdentifierTieBreaker()
    {
        var query = new ClientDirectoryQuery(Sort: "");

        Assert.True(query.Validate().IsSuccess);
        Assert.Empty(query.ReadSorts().Value);
    }

    [Fact]
    public async Task Read_CancelledRequest_DoesNotReadStore()
    {
        var store = new Store();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await new ClientDirectoryService(store).ReadAsync(new(),
            Actor(new(true, PermissionDataScope.Available, PermissionTimeScope.Any)), cancellation.Token));

        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task Read_CancellationDuringStoreIo_ForwardsCallerTokenAndStopsWaiting()
    {
        var store = new PendingStore();
        using var cancellation = new CancellationTokenSource();
        var operation = new ClientDirectoryService(store).ReadAsync(new(),
            Actor(new(true, PermissionDataScope.Available, PermissionTimeScope.Any)), cancellation.Token);

        var forwarded = await store.Received.Task.WaitAsync(Token);
        Assert.Equal(cancellation.Token, forwarded);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await operation);
    }

    private sealed class Store : IClientDirectoryStore
    {
        internal int Reads;
        internal ClientDirectoryQuery? LastQuery;
        public Task<ClientDirectoryPage> ReadAsync(ClientDirectoryQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            LastQuery = query;
            return Task.FromResult(new ClientDirectoryPage([], query.Page, query.PageSize, 0, 0));
        }
        public Task<ClientDirectoryOptions> ReadOptionsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            return Task.FromResult(new ClientDirectoryOptions([], []));
        }
    }

    private sealed class PendingStore : IClientDirectoryStore
    {
        internal TaskCompletionSource<CancellationToken> Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<ClientDirectoryPage> ReadAsync(ClientDirectoryQuery query, CancellationToken cancellationToken)
        {
            Received.TrySetResult(cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("An infinite delay must not complete normally.");
        }
        public Task<ClientDirectoryOptions> ReadOptionsAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
