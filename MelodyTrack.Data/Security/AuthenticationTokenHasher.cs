using System.Security.Cryptography;
using System.Text;
using MelodyTrack.Core.Configuration;
using Microsoft.Extensions.Options;

namespace MelodyTrack.Data.Security;

public sealed class AuthenticationTokenHasher(IOptions<AuthenticationSecretsOptions> options)
{
    private readonly byte[] _refreshTokenHashKey = AuthenticationSecretMaterial.DecodeSymmetricKey(
        options.Value.RefreshTokenHashKey,
        "AuthenticationSecrets:RefreshTokenHashKey");

    public string HashRefreshToken(string token)
    {
        return Convert.ToHexString(HMACSHA256.HashData(_refreshTokenHashKey, Encoding.UTF8.GetBytes(token)));
    }

    public static string HashOpaqueToken(string token)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }
}
