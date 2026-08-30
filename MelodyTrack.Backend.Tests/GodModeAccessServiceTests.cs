using System.Security.Cryptography;
using MelodyTrack.Backend.GodMode;
using MelodyTrack.Core.Configuration;
using MelodyTrack.Data.Security;
using Microsoft.Extensions.Options;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public sealed class GodModeAccessServiceTests : IDisposable
{
    private readonly string _stateDirectory = Path.Combine(Path.GetTempPath(), $"melodytrack-god-mode-{Guid.NewGuid():N}");
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 8, 29, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task ExchangeAsync_ValidBootstrapToken_ConsumesTokenOnlyOnce()
    {
        var options = CreateOptions();
        var token = await GodModeBootstrapTokenStore.IssueAsync(
            options,
            _timeProvider,
            TestContext.Current.CancellationToken);
        var service = new GodModeAccessService(Options.Create(options), _timeProvider);

        var firstSession = await service.ExchangeAsync(token, TestContext.Current.CancellationToken);
        var secondSession = await service.ExchangeAsync(token, TestContext.Current.CancellationToken);

        firstSession.ShouldNotBeNull();
        secondSession.ShouldBeNull();
    }

    [Fact]
    public async Task ExchangeAsync_ExpiredBootstrapToken_IsRejected()
    {
        var options = CreateOptions();
        var token = await GodModeBootstrapTokenStore.IssueAsync(
            options,
            _timeProvider,
            TestContext.Current.CancellationToken);
        var expiredTimeProvider = new FixedTimeProvider(_timeProvider.GetUtcNow().AddMinutes(6));
        var service = new GodModeAccessService(Options.Create(options), expiredTimeProvider);

        var session = await service.ExchangeAsync(token, TestContext.Current.CancellationToken);

        session.ShouldBeNull();
    }

    [Fact]
    public void ValidateSessionCookie_SignedShortLivedSession_RoundTripsWithoutNormalAuthenticationState()
    {
        var service = new GodModeAccessService(Options.Create(CreateOptions()), _timeProvider);
        var session = new GodModeSession("operator-session", _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(10));

        var cookie = service.CreateSessionCookie(session);
        var parsed = service.ValidateSessionCookie(cookie);

        parsed.ShouldNotBeNull();
        parsed.Id.ShouldBe(session.Id);
    }

    [Fact]
    public void ValidateSessionCookie_TamperedSignature_IsRejected()
    {
        var service = new GodModeAccessService(Options.Create(CreateOptions()), _timeProvider);
        var cookie = service.CreateSessionCookie(new GodModeSession("operator-session", _timeProvider.GetUtcNow().UtcDateTime.AddMinutes(10)));
        var tampered = cookie[..^1] + (cookie[^1] == 'A' ? 'B' : 'A');

        service.ValidateSessionCookie(tampered).ShouldBeNull();
    }

    public void Dispose()
    {
        if (Directory.Exists(_stateDirectory))
        {
            Directory.Delete(_stateDirectory, recursive: true);
        }
    }

    private GodModeOptions CreateOptions() => new()
    {
        StateDirectory = _stateDirectory,
        PublicBaseUrl = "https://god-mode.test",
        SessionSigningKey = $"base64:{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}"
    };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
