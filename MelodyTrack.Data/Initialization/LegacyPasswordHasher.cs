using System.Security.Cryptography;
using System.Text;
using Isopoh.Cryptography.Argon2;

namespace MelodyTrack.Data.Initialization;

internal static class LegacyPasswordHasher
{
    public static string Hash(string password, string legacyPepper)
    {
        var config = new Argon2Config
        {
            Type = Argon2Type.DataIndependentAddressing,
            Version = Argon2Version.Nineteen,
            TimeCost = 3,
            MemoryCost = 3000,
            Password = Encoding.UTF8.GetBytes(password),
            Salt = RandomNumberGenerator.GetBytes(16),
            Secret = Encoding.UTF8.GetBytes(legacyPepper)
        };

        var argon2 = new Argon2(config);
        using var hash = argon2.Hash();
        return config.EncodeString(hash.Buffer);
    }
}
