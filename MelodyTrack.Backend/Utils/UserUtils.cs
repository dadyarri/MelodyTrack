using System.Text;
using Isopoh.Cryptography.Argon2;
using Isopoh.Cryptography.SecureArray;

namespace MelodyTrack.Backend.Utils;

/// <summary>
/// Utils to work with user's sensitive information
/// </summary>
public class UserUtils
{
    /// <summary>
    /// Hash password
    /// </summary>
    /// <param name="email">Email address</param>
    /// <param name="password">Password</param>
    /// <param name="hash">Hashed password</param>
    public static void HashPassword(string email, string password, out string hash)
    {
        var salt = new byte[16];
        Random.Shared.NextBytes(salt);
        var config = new Argon2Config
        {
            Type = Argon2Type.DataIndependentAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = 3,
            MemoryCost = 3000,
            AssociatedData = Encoding.UTF8.GetBytes(email),
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
        var config = new Argon2Config { Password = Encoding.UTF8.GetBytes(password), Threads = 2 };

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
}