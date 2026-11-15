using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Api;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using Xunit;

namespace MelodyTrack.Rewrite.Api.Tests;

public sealed class ClientCardApiTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly Guid ClientId = Guid.Parse("019a4000-0000-7000-8000-000000000101");
    private static readonly Guid SourceId = Guid.Parse("019a4000-0000-7000-8000-000000000102");
    private static readonly ClientCard Card = new(ClientId, "Synthetic", "Example", null,
        new DateOnly(2000, 2, 29), null, null, false,
        new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero), []);
    private static Actor.Staff Actor => new(StaffApiHost.Staff, StaffRole.Superuser,
        InitialPermissionPolicies.ForRole(StaffRole.Superuser));

    [Fact]
    public async Task Create_AnonymousRequest_DoesNotWrite()
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor, Token, cards: store);

        using var response = await host.SendAsync(HttpMethod.Post, "/api/clients", CreateRequest(), Token,
            authenticated: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Create_FlatRequest_ReturnsLocationAndAdvisoryDuplicateFlag()
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor, Token, cards: store);

        using var response = await host.SendAsync(HttpMethod.Post, "/api/clients", CreateRequest(), Token);
        var body = await response.Content.ReadFromJsonAsync<ClientCardCreateResponse>(Token);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/clients/{ClientId:D}", response.Headers.Location!.OriginalString);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(ClientId, body!.Client.Id);
        Assert.True(body.HasDuplicateContacts);
        Assert.Equal(StaffApiHost.Staff, store.Author);
        Assert.Equal(SourceId, store.Values!.SourceId);
    }

    [Fact]
    public async Task Update_LegacyExpectedSourceNull_AcceptsNewSource()
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor, Token, cards: store);
        var expected = new ClientCardValuesRequest(Card.FirstName, Card.LastName, null, Card.Birthdate, null, []);
        var request = new ClientCardUpdateRequest(Guid.NewGuid(), expected, Card.FirstName, Card.LastName,
            null, Card.Birthdate, SourceId, []);

        using var response = await host.SendAsync(HttpMethod.Put, $"/api/clients/{ClientId:D}", request, Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(store.Expected!.Values.SourceId);
        Assert.Equal(SourceId, store.Values!.SourceId);
    }

    [Fact]
    public async Task Update_MissingExpectedState_RejectsWithoutWritingOrEchoingValues()
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor, Token, cards: store);
        var request = new ClientCardUpdateRequest(Guid.NewGuid(), null, "PrivateSyntheticInput", "Example",
            null, null, SourceId, []);

        using var response = await host.SendAsync(HttpMethod.Put, $"/api/clients/{ClientId:D}", request, Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain("PrivateSyntheticInput", await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Read_ExistingCard_ReturnsNoStoreCalendarDate()
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor, Token, cards: store);
        host.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", StaffApiHost.BearerToken);

        using var response = await host.Client.GetAsync($"/api/clients/{ClientId:D}", Token);
        var body = await response.Content.ReadFromJsonAsync<ClientCard>(Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(new DateOnly(2000, 2, 29), body!.Birthdate);
        Assert.Null(body.SourceId);
    }

    [Fact]
    public async Task Archive_AnyClient_ReturnsDependencyConflictWithoutWriting()
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor, Token, cards: store);

        using var response = await host.SendAsync(HttpMethod.Post, $"/api/clients/{ClientId:D}/archive",
            new ClientArchiveRequest(Guid.NewGuid()), Token);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("client.archive_dependencies_not_migrated", await response.Content.ReadAsStringAsync(Token));
        Assert.Equal(0, store.Writes);
    }

    [Fact]
    public async Task Restore_ArchivedClient_UsesAuthenticatedAuthor()
    {
        var store = new Store();
        await using var host = await StaffApiHost.StartAsync(Actor, Token, cards: store);

        using var response = await host.SendAsync(HttpMethod.Post, $"/api/clients/{ClientId:D}/restore",
            new ClientArchiveRequest(Guid.NewGuid()), Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(StaffApiHost.Staff, store.Author);
        Assert.Equal(1, store.Writes);
    }

    private static ClientCardCreateRequest CreateRequest() => new(Guid.NewGuid(), ClientId,
        Card.FirstName, Card.LastName, null, Card.Birthdate, SourceId, []);

    private sealed class Store : IClientCardsStore
    {
        internal int Writes;
        internal StaffId Author;
        internal ClientCardValues? Values;
        internal ClientCardExpectedState? Expected;

        public Task<Result<ClientCard, ErrorList>> ReadAsync(ClientId clientId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<ClientCard, ErrorList>(Card));

        public Task<Result<ClientCardCommandResult, ErrorList>> CreateAsync(Guid commandId, StaffId authorId,
            ClientId clientId, ClientCardValues values, byte[] requestFingerprint, DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            Writes++;
            Author = authorId;
            Values = values;
            return Task.FromResult(Result.Success<ClientCardCommandResult, ErrorList>(new(Card, true)));
        }

        public Task<Result<ClientCardCommandResult, ErrorList>> UpdateAsync(Guid commandId, StaffId authorId,
            ClientId clientId, ClientCardExpectedState expectedState, ClientCardValues values,
            byte[] requestFingerprint, DateTimeOffset now, CancellationToken cancellationToken)
        {
            Writes++;
            Author = authorId;
            Expected = expectedState;
            Values = values;
            return Task.FromResult(Result.Success<ClientCardCommandResult, ErrorList>(new(null, false)));
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
