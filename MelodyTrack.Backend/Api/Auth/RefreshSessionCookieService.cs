using System.Security.Cryptography;
using System.Text;
using MelodyTrack.Core.Configuration;
using MelodyTrack.Data.Security;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Backend.Api.Auth;

public sealed class RefreshSessionCookieService(
    IOptions<AuthenticationSecretsOptions> authenticationSecrets,
    IHostEnvironment environment,
    TimeProvider timeProvider)
{
    public const string RefreshCookieName = "MelodyTrack.Refresh";
    public const string CsrfCookieName = "MelodyTrack.Csrf";
    public const string CsrfHeaderName = "X-CSRF-Token";

    private readonly byte[] _csrfSigningKey = AuthenticationSecretMaterial.DecodeSymmetricKey(
        authenticationSecrets.Value.CsrfSigningKey,
        "AuthenticationSecrets:CsrfSigningKey");

    public string? ReadRefreshToken(HttpRequest request)
    {
        return request.Cookies.TryGetValue(RefreshCookieName, out var token) && !string.IsNullOrWhiteSpace(token)
            ? token
            : null;
    }

    public bool HasValidCsrfToken(HttpRequest request, string refreshToken)
    {
        if (!request.Cookies.TryGetValue(CsrfCookieName, out var cookieToken)
            || !request.Headers.TryGetValue(CsrfHeaderName, out var headerValues))
        {
            return false;
        }

        var headerToken = headerValues.ToString();
        var expectedToken = CreateCsrfToken(refreshToken);
        return FixedTimeEquals(cookieToken, headerToken) && FixedTimeEquals(expectedToken, headerToken);
    }

    public void Issue(HttpResponse response, string refreshToken, DateTime validUntilUtc)
    {
        var options = CreateCookieOptions(validUntilUtc, httpOnly: true);
        response.Cookies.Append(RefreshCookieName, refreshToken, options);
        response.Cookies.Append(CsrfCookieName, CreateCsrfToken(refreshToken), CreateCookieOptions(validUntilUtc, httpOnly: false));
    }

    public void Clear(HttpResponse response)
    {
        var options = CreateCookieOptions(timeProvider.GetUtcNow().UtcDateTime.AddDays(-1), httpOnly: true);
        response.Cookies.Delete(RefreshCookieName, options);
        response.Cookies.Delete(CsrfCookieName, CreateCookieOptions(options.Expires!.Value.UtcDateTime, httpOnly: false));
    }

    private CookieOptions CreateCookieOptions(DateTime validUntilUtc, bool httpOnly)
    {
        var expires = DateTime.SpecifyKind(validUntilUtc, DateTimeKind.Utc);
        return new CookieOptions
        {
            HttpOnly = httpOnly,
            Secure = !environment.IsDevelopment() && !environment.IsEnvironment("Test"),
            SameSite = SameSiteMode.Strict,
            IsEssential = true,
            Path = "/",
            Expires = new DateTimeOffset(expires)
        };
    }

    internal string CreateCsrfToken(string refreshToken)
    {
        var hash = HMACSHA256.HashData(_csrfSigningKey, Encoding.UTF8.GetBytes(refreshToken));
        return WebEncoders.Base64UrlEncode(hash);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
