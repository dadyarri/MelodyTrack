using System.Security.Cryptography;
using System.Text;

namespace MelodyTrack.Backend.Services;

public sealed class PersonalDataProtector : IPersonalDataProtector
{
    private const string Prefix = "enc:";
    private readonly string _currentVersion;
    private readonly IReadOnlyDictionary<string, byte[]> _keysByVersion;

    public PersonalDataProtector(string currentVersion, IReadOnlyDictionary<string, string> masterKeysByVersion)
    {
        _currentVersion = currentVersion;
        _keysByVersion = masterKeysByVersion.ToDictionary(
            pair => pair.Key,
            pair => SHA256.HashData(Encoding.UTF8.GetBytes($"melodytrack:pii:{pair.Value}")),
            StringComparer.Ordinal);
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

        using var aes = new AesGcm(_keysByVersion[_currentVersion], 16);
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        return string.Concat(
            Prefix,
            _currentVersion,
            ':',
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

        var (version, parts) = ParseEncryptedValue(storedValue);
        if (!_keysByVersion.TryGetValue(version, out var key))
        {
            throw new CryptographicException($"Encrypted personal data references unknown key version '{version}'.");
        }

        if (parts.Length != 3)
        {
            throw new CryptographicException("Encrypted personal data has invalid format.");
        }

        var nonce = Convert.FromBase64String(parts[0]);
        var ciphertext = Convert.FromBase64String(parts[1]);
        var tag = Convert.FromBase64String(parts[2]);
        var plaintext = new byte[ciphertext.Length];

        using var aes = new AesGcm(key, 16);
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Encoding.UTF8.GetString(plaintext);
    }

    public bool IsEncrypted(string storedValue)
    {
        return storedValue.StartsWith(Prefix, StringComparison.Ordinal);
    }

    public bool ShouldReencrypt(string storedValue)
    {
        if (!IsEncrypted(storedValue))
        {
            return false;
        }

        var (version, _) = ParseEncryptedValue(storedValue);
        return !string.Equals(version, _currentVersion, StringComparison.Ordinal);
    }

    private static (string Version, string[] Parts) ParseEncryptedValue(string storedValue)
    {
        var payload = storedValue[Prefix.Length..];
        var parts = payload.Split(':', 4);
        if (parts.Length != 4)
        {
            throw new CryptographicException("Encrypted personal data has invalid format.");
        }

        return (parts[0], [parts[1], parts[2], parts[3]]);
    }
}
