using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;
using Isopoh.Cryptography.SecureArray;
using MelodyTrack.Core.Configuration;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Data.Security;

public sealed class CredentialHasher(IOptions<AuthenticationSecretsOptions> options)
{
    private const string FormatPrefix = "mt-argon2id-v1$";
    private const int MemoryCostKiB = 65_536;
    private const int TimeCost = 3;
    private const int Parallelism = 4;

    private readonly byte[] _passwordPepper = AuthenticationSecretMaterial.DecodeSymmetricKey(
        options.Value.PasswordPepper,
        "AuthenticationSecrets:PasswordPepper");
    private readonly byte[] _portalPinPepper = AuthenticationSecretMaterial.DecodeSymmetricKey(
        options.Value.PortalPinPepper,
        "AuthenticationSecrets:PortalPinPepper");

    public string HashPassword(string password) => Hash(password, _passwordPepper);

    public bool VerifyPassword(string encodedHash, string password) => Verify(encodedHash, password, _passwordPepper);

    public string HashPortalPin(string pin) => Hash(pin, _portalPinPepper);

    public bool VerifyPortalPin(string encodedHash, string pin) => Verify(encodedHash, pin, _portalPinPepper);

    public static bool NeedsRehash(string encodedHash) => !encodedHash.StartsWith(FormatPrefix, StringComparison.Ordinal);

    private static string Hash(string value, byte[] pepper)
    {
        var config = CreateConfig(value, pepper);
        using var argon2 = new Argon2(config);
        using var hash = argon2.Hash();
        return FormatPrefix + config.EncodeString(hash.Buffer);
    }

    private static bool Verify(string encodedHash, string value, byte[] pepper)
    {
        if (!encodedHash.StartsWith(FormatPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var config = CreateConfig(value, pepper);
        SecureArray<byte>? expectedHash = null;
        try
        {
            if (!config.DecodeString(encodedHash[FormatPrefix.Length..], out expectedHash))
            {
                return false;
            }

            using var argon2 = new Argon2(config);
            using var actualHash = argon2.Hash();
            return Argon2.FixedTimeEquals(expectedHash, actualHash);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }
        finally
        {
            expectedHash?.Dispose();
        }
    }

    private static Argon2Config CreateConfig(string value, byte[] pepper)
    {
        return new Argon2Config
        {
            Type = Argon2Type.HybridAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = TimeCost,
            MemoryCost = MemoryCostKiB,
            Lanes = Parallelism,
            Threads = Parallelism,
            Password = Encoding.UTF8.GetBytes(value),
            Salt = RandomNumberGenerator.GetBytes(16),
            Secret = pepper
        };
    }
}
