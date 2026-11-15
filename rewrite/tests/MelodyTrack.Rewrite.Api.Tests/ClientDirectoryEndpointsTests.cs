using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class ClientDirectoryEndpointsTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Read_AnonymousRequest_DoesNotReadDirectory()
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor(StaffRole.Superuser), Token, directory: store);

        using var response = await host.Client.GetAsync("/api/clients", Token);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task Read_ValidFilters_ReturnsNoStorePageAndTypedParameters()
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor(StaffRole.Administrator), Token, directory: store);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);

        using var response = await host.Client.GetAsync("/api/clients?q=Example&archive=all&page=2&pageSize=50&sort=createdAt:desc,name:asc", Token);
        var body = await response.Content.ReadFromJsonAsync<ClientDirectoryPage>(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(2, body!.Page);
        Assert.Equal(50, store.LastQuery!.PageSize);
        Assert.Equal("createdAt:desc,name:asc", store.LastQuery.Sort);
        Assert.Equal("Example", store.LastQuery.Q);
    }

    [Fact]
    public async Task Read_EntrySerializesDateOnlyAndUtcInstantWithoutLocalShift()
    {
        var store = new Store
        {
            PageResult = new([
                new ClientDirectoryEntry(Guid.Parse("019a2c63-8c00-7000-8000-000000000001"), "First", "Synthetic", null,
                    new DateOnly(2000, 2, 29), null, null, false,
                    new DateTimeOffset(2026, 11, 1, 21, 0, 0, TimeSpan.Zero), [])
            ], 1, 10, 1, 1)
        };
        await using var host = await StaffApiHost.StartAsync(Actor(StaffRole.Superuser), Token, directory: store);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);

        using var response = await host.Client.GetAsync("/api/clients", Token);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Token));
        var entry = body.RootElement.GetProperty("items")[0];
        var date = entry.GetProperty("birthdate").GetString();
        var createdAt = entry.GetProperty("createdAt").GetDateTimeOffset();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("2000-02-29", date);
        Assert.Equal(TimeSpan.Zero, createdAt.Offset);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 21, 0, 0, TimeSpan.Zero), createdAt);
    }

    [Fact]
    public async Task ReadOptions_ReturnsTypedOptionsWithoutCaching()
    {
        var sourceId = Guid.Parse("019a2c63-8c00-7000-8000-000000000021");
        var kindId = Guid.Parse("019a2c63-8c00-7000-8000-000000000022");
        var store = new Store
        {
            OptionsResult = new([new(sourceId, "Synthetic source")], [new(kindId, "Synthetic email", "email", true)])
        };
        await using var host = await StaffApiHost.StartAsync(Actor(StaffRole.Superuser), Token, directory: store);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);

        using var response = await host.Client.GetAsync("/api/clients/options", Token);
        var options = await response.Content.ReadFromJsonAsync<ClientDirectoryOptions>(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(sourceId, Assert.Single(options!.Sources).Id);
        var kind = Assert.Single(options.ContactKinds);
        Assert.Equal(kindId, kind.Id);
        Assert.Equal("email", kind.Format);
        Assert.True(kind.IsArchived);
        Assert.Equal(1, store.OptionReads);
    }

    [Theory]
    [InlineData("page=0")]
    [InlineData("pageSize=101")]
    [InlineData("sort=unrecognized")]
    [InlineData("sort=name:asc,name:desc")]
    [InlineData("sort=name:asc,")]
    public async Task Read_InvalidQuery_ReturnsSafeValidationProblem(string query)
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor(StaffRole.Superuser), Token, directory: store);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);

        using var response = await host.Client.GetAsync($"/api/clients?q=private-synthetic-value&{query}", Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.DoesNotContain("private-synthetic-value", await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(0, store.Reads);
    }

    [Theory]
    [InlineData("/api/clients")]
    [InlineData("/api/clients/options")]
    public async Task Read_OwnScope_DeniesInsteadOfWideningOrReturningEmpty(string path)
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor(StaffRole.Teacher), Token, directory: store);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);

        using var response = await host.Client.GetAsync(path, Token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task Read_DefaultQuery_UsesTenRowsAndStableNameOrder()
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor(StaffRole.Superuser), Token, directory: store);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);

        using var response = await host.Client.GetAsync("/api/clients", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(10, store.LastQuery!.PageSize);
        Assert.Equal(new ClientDirectorySort("name", false), Assert.Single(store.LastQuery.ReadSorts().Value));
    }

    [Fact]
    public async Task Read_ExplicitEmptySort_LeavesNoImplicitColumnSort()
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor(StaffRole.Superuser), Token, directory: store);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);

        using var response = await host.Client.GetAsync("/api/clients?sort=", Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(store.LastQuery!.ReadSorts().Value);
    }

    [Theory]
    [InlineData(200, HttpStatusCode.OK)]
    [InlineData(201, HttpStatusCode.BadRequest)]
    public async Task Read_QueryLength_EnforcesInclusiveLimitAtHttpBoundary(int length, HttpStatusCode expected)
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor(StaffRole.Superuser), Token, directory: store);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);

        using var response = await host.Client.GetAsync($"/api/clients?q={new string('x', length)}", Token);

        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(expected == HttpStatusCode.OK ? 1 : 0, store.Reads);
    }

    private static Actor.Staff Actor(StaffRole role) => new(StaffApiHost.Staff, role, InitialPermissionPolicies.ForRole(role));

    private sealed class Store : IClientDirectoryStore
    {
        internal int Reads;
        internal int OptionReads;
        internal ClientDirectoryQuery? LastQuery;
        internal ClientDirectoryPage? PageResult;
        internal ClientDirectoryOptions? OptionsResult;
        public Task<ClientDirectoryPage> ReadAsync(ClientDirectoryQuery query, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            LastQuery = query;
            return Task.FromResult(PageResult ?? new ClientDirectoryPage([], query.Page, query.PageSize, 0, 0));
        }
        public Task<ClientDirectoryOptions> ReadOptionsAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Reads++;
            OptionReads++;
            return Task.FromResult(OptionsResult ?? new ClientDirectoryOptions([], []));
        }
    }
}
