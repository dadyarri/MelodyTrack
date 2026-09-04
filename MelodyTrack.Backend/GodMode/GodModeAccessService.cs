using System.Security.Cryptography;
using System.Text;
using MelodyTrack.Core.Configuration;
using MelodyTrack.Data.Security;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Backend.GodMode;

public sealed record GodModeSession(string Id, DateTime ExpiresAtUtc);

public sealed class GodModeAccessService(IOptions<GodModeOptions> options, TimeProvider timeProvider)
{
    public const string CookieName = "MelodyTrack.GodMode";
    private readonly GodModeOptions _options = options.Value;
    private readonly byte[] _sessionSigningKey = AuthenticationSecretMaterial.DecodeSymmetricKey(
        options.Value.SessionSigningKey,
        "GodMode:SessionSigningKey");

    public async Task<GodModeSession?> ExchangeAsync(string token, CancellationToken ct)
    {
        if (!await GodModeBootstrapTokenStore.ConsumeAsync(_options, token, timeProvider, ct))
        {
            return null;
        }

        return new GodModeSession(
            WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(18)),
            timeProvider.GetUtcNow().UtcDateTime.AddMinutes(_options.SessionLifetimeMinutes));
    }

    public string CreateSessionCookie(GodModeSession session)
    {
        var payload = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes($"{session.Id}|{new DateTimeOffset(session.ExpiresAtUtc).ToUnixTimeSeconds()}"));
        var signature = WebEncoders.Base64UrlEncode(HMACSHA256.HashData(_sessionSigningKey, Encoding.UTF8.GetBytes(payload)));
        return $"{payload}.{signature}";
    }

    public GodModeSession? ValidateSessionCookie(string? cookie)
    {
        var parts = cookie?.Split('.', 2);
        if (parts is not { Length: 2 })
        {
            return null;
        }

        try
        {
            var suppliedSignature = WebEncoders.Base64UrlDecode(parts[1]);
            var expectedSignature = HMACSHA256.HashData(_sessionSigningKey, Encoding.UTF8.GetBytes(parts[0]));
            if (!CryptographicOperations.FixedTimeEquals(suppliedSignature, expectedSignature))
            {
                return null;
            }

            var payload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(parts[0])).Split('|', 2);
            if (payload.Length != 2 || !long.TryParse(payload[1], out var expiresUnix))
            {
                return null;
            }

            var expiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(expiresUnix).UtcDateTime;
            return expiresAtUtc > timeProvider.GetUtcNow().UtcDateTime
                ? new GodModeSession(payload[0], expiresAtUtc)
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

}
