using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MelodyTrack.Backend.Api.Auth;
using MelodyTrack.Backend.Data.Models;
using MelodyTrack.Core.Configuration;
using MelodyTrack.Data.Security;
using Microsoft.Extensions.Options;
using OtpNet;
using QRCoder;

namespace MelodyTrack.Backend.Utils;

/// <summary>
///     Utils to work with user's sensitive information
/// </summary>
public static class UserUtils
{
    private static CredentialHasher? _credentialHasher;
    private static AuthenticationTokenHasher? _tokenHasher;
    private static JwtTokenService? _jwtTokenService;
    private static string? _personalDataKey;

    public static void ConfigureAuthentication(
        AuthenticationSecretsOptions authenticationSecrets,
        JwtOptions jwtOptions,
        string personalDataKey)
    {
        var secretOptions = Options.Create(authenticationSecrets);
        _credentialHasher = new CredentialHasher(secretOptions);
        _tokenHasher = new AuthenticationTokenHasher(secretOptions);
        _jwtTokenService = new JwtTokenService(secretOptions, Options.Create(jwtOptions));
        _personalDataKey = personalDataKey;
    }

    /// <summary>
    ///     Hash password
    /// </summary>
    /// <param name="password">Password</param>
    /// <param name="hash">Hashed password</param>
    public static void HashPassword(string password, out string hash)
    {
        hash = GetCredentialHasher().HashPassword(password);
    }

    public static bool IsValidPassword(string hash, string password) => GetCredentialHasher().VerifyPassword(hash, password);

    public static string HashPortalPin(string pin) => GetCredentialHasher().HashPortalPin(pin);

    public static bool IsValidPortalPin(string hash, string pin) => GetCredentialHasher().VerifyPortalPin(hash, pin);

    public static string GenerateRandomString(int length)
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var bytes = new byte[length];
        RandomNumberGenerator.Fill(bytes);
        var sb = new StringBuilder();
        for (var j = 0; j < length; j++)
        {
            sb.Append(chars[bytes[j] % chars.Length]);
        }

        return sb.ToString();
    }

    public static IEnumerable<string> GenerateRecoveryCodes()
    {
        const int numberOfCodes = 10;
        const int codeLength = 10;
        var codes = new List<string>();

        for (var i = 0; i < numberOfCodes; i++)
        {
            codes.Add(GenerateRandomString(codeLength));
        }

        return codes;
    }

    public static string HashOpaqueToken(string token)
    {
        return AuthenticationTokenHasher.HashOpaqueToken(token);
    }

    public static string HashRefreshToken(string token) => GetTokenHasher().HashRefreshToken(token);

    public static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    public static string HashEmailBlindIndex(string email)
    {
        var normalizedEmail = NormalizeEmail(email);
        var indexKey = SHA256.HashData(
            Encoding.UTF8.GetBytes($"melodytrack:email-index:{GetPersonalDataKey()}"));
        var emailBytes = Encoding.UTF8.GetBytes(normalizedEmail);
        var hash = HMACSHA256.HashData(indexKey, emailBytes);
        return Convert.ToHexString(hash);
    }

    public static string DescribeEmailForLogs(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return "email#unknown";
        }

        var blindIndex = HashEmailBlindIndex(email);
        return $"email#{blindIndex[..12]}";
    }

    public static string DescribeOpaqueValueForLogs(string prefix, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return $"{prefix}#unknown";
        }

        var reference = HashOpaqueToken(value);
        return $"{prefix}#{reference[..12]}";
    }

    public static string DescribeInviteCodeForLogs(Ulid code)
    {
        return DescribeOpaqueValueForLogs("invite", code.ToString());
    }

    public static string CreateAccessToken(User user, Ulid? sessionId = null, TimeProvider? timeProvider = null)
    {
        return GetJwtTokenService().CreateAccessToken(user, sessionId, timeProvider);
    }

    public static (string Secret, string OtpUrl) GenerateTotp(string email)
    {
        var secretBytes = new byte[16];
        RandomNumberGenerator.Fill(secretBytes);
        var secret = Base32Encoding.ToString(secretBytes);

        var generator = new PayloadGenerator.OneTimePassword
        {
            Secret = secret,
            Issuer = "MelodyTrack",
            Label = email,
            AuthAlgorithm = PayloadGenerator.OneTimePassword.OneTimePasswordAuthAlgorithm.SHA1
        };

        return (secret, generator.ToString());
    }

    public static bool VerifyTotpCode(string secret, string? otp)
    {
        if (string.IsNullOrWhiteSpace(otp))
        {
            return false;
        }

        var secretKey = Base32Encoding.ToBytes(secret.Trim().Replace(" ", string.Empty));
        var window = new VerificationWindow(1, 1);

        var sha1Totp = new Totp(secretKey, mode: OtpHashMode.Sha1);
        if (sha1Totp.VerifyTotp(otp, out _, window))
        {
            return true;
        }

        var sha512Totp = new Totp(secretKey, mode: OtpHashMode.Sha512);
        return sha512Totp.VerifyTotp(otp, out _, window);
    }

    private static CredentialHasher GetCredentialHasher()
    {
        return _credentialHasher ?? throw new InvalidOperationException("Authentication crypto has not been configured.");
    }

    private static AuthenticationTokenHasher GetTokenHasher() =>
        _tokenHasher ?? throw new InvalidOperationException("Authentication crypto has not been configured.");

    private static JwtTokenService GetJwtTokenService() =>
        _jwtTokenService ?? throw new InvalidOperationException("Authentication crypto has not been configured.");

    private static string GetPersonalDataKey()
    {
        return _personalDataKey
               ?? Environment.GetEnvironmentVariable("PersonalData__CurrentKey")
               ?? EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEY");
    }

}
