using System.Security.Cryptography;
using System.Text;
using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain;
using Xunit;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class StaffAccessTokensTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly StaffId Staff = new(Guid.Parse("019a2c63-8c00-7000-8000-000000000001"));

    [Fact]
    public async Task IssueAfterSignInAsync_ValidIdentity_StoresOnlyHashAndAuthenticatesIssuedSecret()
    {
        var store = new Store();
        var tokens = new StaffAccessTokens(store);
        var session = Guid.NewGuid();

        var issued = await tokens.IssueAfterSignInAsync(Staff, session, Token);

        Assert.True(issued.IsSuccess);
        Assert.Equal(64, issued.Value.Value.Length);
        Assert.NotEqual(issued.Value.Value, store.Hash);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(issued.Value.Value))), store.Hash);
        Assert.Equal(session, store.Session);
        Assert.Equal(TimeSpan.FromMinutes(5), issued.Value.Lifetime);
        Assert.DoesNotContain(issued.Value.Value, issued.Value.ToString());
        Assert.Equal(new StaffSessionIdentity(Staff, session), await tokens.AuthenticateAsync(issued.Value.Value, Token));
    }

    [Fact]
    public async Task IssueAfterSignInAsync_TwoTokens_UsesIndependentSecrets()
    {
        var tokens = new StaffAccessTokens(new Store());

        var first = await tokens.IssueAfterSignInAsync(Staff, Guid.NewGuid(), Token);
        var second = await tokens.IssueAfterSignInAsync(Staff, Guid.NewGuid(), Token);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.NotEqual(first.Value.Value, second.Value.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG")]
    public async Task AuthenticateAsync_MalformedSecret_DeniesBeforeStore(string value)
    {
        var store = new Store();

        var result = await new StaffAccessTokens(store).AuthenticateAsync(value, Token);

        Assert.Null(result);
        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task AuthenticateAsync_Cancelled_StopsBeforeStore()
    {
        var store = new Store();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new StaffAccessTokens(store)
            .AuthenticateAsync(new string('A', 64), cancellation.Token));

        Assert.Equal(0, store.Reads);
    }

    [Fact]
    public async Task SignOutAsync_AuthenticatedIdentity_RevokesOnlyCurrentSession()
    {
        var store = new Store();
        var tokens = new StaffAccessTokens(store);
        var session = Guid.NewGuid();
        var issued = await tokens.IssueAfterSignInAsync(Staff, session, Token);
        Assert.True(issued.IsSuccess);
        var identity = Assert.IsType<StaffSessionIdentity>(await tokens.AuthenticateAsync(issued.Value.Value, Token));

        var result = await tokens.SignOutAsync(identity, Token);

        Assert.True(result.IsSuccess);
        Assert.Equal(session, store.RevokedSession);
        Assert.Null(await tokens.AuthenticateAsync(issued.Value.Value, Token));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SignOutAsync_EmptyIdentifier_DeniesBeforeStore(bool emptyStaff)
    {
        var store = new Store();
        var identity = new StaffSessionIdentity(emptyStaff ? new StaffId(Guid.Empty) : Staff,
            emptyStaff ? Guid.NewGuid() : Guid.Empty);

        var result = await new StaffAccessTokens(store).SignOutAsync(identity, Token);

        Assert.True(result.IsFailure);
        Assert.Null(store.RevokedSession);
    }

    [Fact]
    public async Task SignOutAsync_Cancelled_StopsBeforeStore()
    {
        var store = new Store();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new StaffAccessTokens(store)
            .SignOutAsync(new(Staff, Guid.NewGuid()), cancellation.Token));

        Assert.Null(store.RevokedSession);
    }

    private sealed class Store : IStaffAccessTokenStore
    {
        internal string? Hash;
        internal Guid Session;
        internal StaffId? Subject;
        internal Guid? RevokedSession;
        internal int Reads;
        public Task<StaffSessionIdentity?> FindStaffAsync(string tokenHash, CancellationToken cancellationToken)
        {
            Reads++;
            return Task.FromResult(tokenHash == Hash && RevokedSession != Session && Subject is { } staff
                ? new StaffSessionIdentity(staff, Session) : null);
        }

        public Task<UnitResult<ErrorList>> AddAsync(string tokenHash, StaffId staffId, Guid sessionId, CancellationToken cancellationToken)
        {
            Hash = tokenHash;
            Subject = staffId;
            Session = sessionId;
            return Task.FromResult(UnitResult.Success<ErrorList>());
        }

        public Task RevokeSessionAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            RevokedSession = sessionId;
            return Task.CompletedTask;
        }
    }
}
