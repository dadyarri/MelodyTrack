using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FastEndpoints.Security;
using Isopoh.Cryptography.Argon2;
using Isopoh.Cryptography.SecureArray;
using MelodyTrack.Backend.Data.Models;
using Microsoft.AspNetCore.WebUtilities;
using OtpNet;
using QRCoder;

namespace MelodyTrack.Backend.Utils;

/// <summary>
///     Utils to work with user's sensitive information
/// </summary>
public static class UserUtils
{
    /// <summary>
    ///     Hash password
    /// </summary>
    /// <param name="password">Password</param>
    /// <param name="hash">Hashed password</param>
    public static void HashPassword(string password, out string hash)
    {
        var salt = new byte[16];
        Random.Shared.NextBytes(salt);
        var config = new Argon2Config
        {
            Type = Argon2Type.DataIndependentAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = 3,
            MemoryCost = 3000,
            Password = Encoding.UTF8.GetBytes(password),
            Salt = salt,
            Secret = Encoding.UTF8.GetBytes(
                EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_JWT_SIGNING_KEY"))
        };

        var argon2 = new Argon2(config);
        using var hashA = argon2.Hash();
        hash = config.EncodeString(hashA.Buffer);
    }

    public static bool IsValidPassword(string hash, string password)
    {
        var config = new Argon2Config
        {
            Password = Encoding.UTF8.GetBytes(password),
            Secret = Encoding.UTF8.GetBytes(EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_JWT_SIGNING_KEY"))
        };

        SecureArray<byte>? hashA = null;
        try
        {
            if (config.DecodeString(hash, out hashA))
            {
                var argon2 = new Argon2(config);
                using var hashToVerify = argon2.Hash();
                return Argon2.FixedTimeEquals(hashA, hashToVerify);
            }
        }
        finally
        {
            hashA?.Dispose();
        }

        return false;
    }

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
        var secret = Encoding.UTF8.GetBytes(EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_JWT_SIGNING_KEY"));
        var tokenBytes = Encoding.UTF8.GetBytes(token);
        var hash = HMACSHA256.HashData(secret, tokenBytes);
        return Convert.ToHexString(hash);
    }

    public static string NormalizeEmail(string email)
    {
        return email.Trim().ToLowerInvariant();
    }

    public static string HashEmailBlindIndex(string email)
    {
        var normalizedEmail = NormalizeEmail(email);
        var indexKey = SHA256.HashData(
            Encoding.UTF8.GetBytes($"melodytrack:email-index:{EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_PII_MASTER_KEY")}"));
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

    public static string CreateAccessToken(User user, Ulid? sessionId = null)
    {
        var expireAt = DateTime.UtcNow.AddMinutes(10);
        return JwtBearer.CreateToken(opts =>
        {
            opts.SigningKey = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_JWT_SIGNING_KEY");
            opts.Issuer = "MelodyTrack";
            opts.ExpireAt = expireAt;
            opts.User.Claims.Add(new Claim(ClaimTypes.Name, user.Email));

            if (sessionId.HasValue)
            {
                opts.User.Claims.Add(new Claim(ClaimTypes.Sid, sessionId.Value.ToString()));
            }
        });
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

    public static string GetInviteUrl(Ulid code)
    {
        var appDomain = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_APP_DOMAIN");
        return $"{appDomain}/invite/{code}";
    }

    public static string GetResetPasswordUrl(string token)
    {
        var appDomain = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_APP_DOMAIN");
        return $"{appDomain}/restore?code={token}";
    }

    public static string GetClientPortalAccessUrl(string token)
    {
        var appDomain = EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_APP_DOMAIN");
        return $"{appDomain}/portal/access/{token}";
    }

    public static string CreateClientPortalToken(Ulid clientId)
    {
        var clientIdValue = clientId.ToString();
        var secret = Encoding.UTF8.GetBytes(EnvironmentUtils.GetRequiredEnvironmentVariable("MELODY_TRACK_JWT_SIGNING_KEY"));
        var signature = HMACSHA256.HashData(secret, Encoding.UTF8.GetBytes($"client-portal:{clientIdValue}"));
        return $"{clientIdValue}.{WebEncoders.Base64UrlEncode(signature)}";
    }

    public static bool TryReadClientPortalToken(string token, out Ulid clientId)
    {
        clientId = default;

        var parts = token.Split('.', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || !Ulid.TryParse(parts[0], out clientId))
        {
            return false;
        }

        var expectedToken = CreateClientPortalToken(clientId);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expectedToken),
            Encoding.UTF8.GetBytes(token));
    }
}
