using System.Security.Cryptography;
using System.Text;

namespace MelodyTrack.Backend.Services;

public sealed class PersonalDataProtector : IPersonalDataProtector
{
    private const string Prefix = "enc:v1:";
    private readonly byte[] _key;

    public PersonalDataProtector(string masterKey)
    {
        _key = SHA256.HashData(Encoding.UTF8.GetBytes($"melodytrack:pii:{masterKey}"));
    }

    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            return plaintext;
        }

        var nonce = RandomNumberGenerator.GetBytes(12);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[16];

        using var aes = new AesGcm(_key, 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return string.Concat(
            Prefix,
            Convert.ToBase64String(nonce),
            ':',
            Convert.ToBase64String(ciphertext),
            ':',
            Convert.ToBase64String(tag));
    }

    public string Decrypt(string storedValue)
    {
        if (string.IsNullOrEmpty(storedValue) || !IsEncrypted(storedValue))
        {
            return storedValue;
        }

        var payload = storedValue[Prefix.Length..];
        var parts = payload.Split(':', 3);
        if (parts.Length != 3)
        {
            throw new CryptographicException("Encrypted personal data has invalid format.");
        }

        var nonce = Convert.FromBase64String(parts[0]);
        var ciphertext = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(_key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    public bool IsEncrypted(string storedValue)
    {
        return storedValue.StartsWith(Prefix, StringComparison.Ordinal);
    }
}
