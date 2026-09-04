using System.Security.Cryptography;

namespace MelodyTrack.Data.Security;

public static class AuthenticationSecretMaterial
{
    private const string Base64Prefix = "base64:";

    public static byte[] DecodeSymmetricKey(string value, string configurationName)
    {
        if (!value.StartsWith(Base64Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"{configurationName} must use the 'base64:' format.");
        }

        try
        {
            var key = Convert.FromBase64String(value[Base64Prefix.Length..]);
            if (key.Length < 32)
            {
                throw new InvalidOperationException($"{configurationName} must contain at least 32 bytes of key material.");
            }

            return key;
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException($"{configurationName} must contain valid base64 key material.", exception);
        }
    }

    public static byte[] DecodeP256PrivateKey(string value)
    {
        if (!value.StartsWith(Base64Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("AuthenticationSecrets:JwtSigningPrivateKey must use the 'base64:' PKCS#8 format.");
        }

        byte[] privateKey;
        try
        {
            privateKey = Convert.FromBase64String(value[Base64Prefix.Length..]);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("AuthenticationSecrets:JwtSigningPrivateKey must contain valid base64 PKCS#8 data.", exception);
        }

        using var ecdsa = ECDsa.Create();
        try
        {
            ecdsa.ImportPkcs8PrivateKey(privateKey, out var bytesRead);
            if (bytesRead != privateKey.Length || ecdsa.KeySize != 256)
            {
                throw new InvalidOperationException("AuthenticationSecrets:JwtSigningPrivateKey must be a P-256 private key.");
            }
        }
        catch (CryptographicException exception)
        {
            throw new InvalidOperationException("AuthenticationSecrets:JwtSigningPrivateKey must be a valid P-256 PKCS#8 private key.", exception);
        }

        return privateKey;
    }
}
