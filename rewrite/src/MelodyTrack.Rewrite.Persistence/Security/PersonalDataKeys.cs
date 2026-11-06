using System.Security.Cryptography;
using System.Text;

namespace MelodyTrack.Rewrite.Persistence.Security;

internal sealed class PersonalDataKeys : IDisposable
{
    private readonly Dictionary<string, byte[]> keys = new(StringComparer.Ordinal);
    private bool disposed;

    private PersonalDataKeys(IReadOnlyDictionary<string, string> values, bool legacy)
    {
        ArgumentNullException.ThrowIfNull(values);
        try
        {
            foreach (var (version, value) in values)
            {
                var validVersion = legacy
                    ? !string.IsNullOrWhiteSpace(version) && version.All(character => char.IsLetterOrDigit(character) || character is '-' or '_')
                    : ValidVersion(version);
                if (!validVersion || value is null || (!legacy && string.IsNullOrWhiteSpace(value)))
                    throw InvalidConfiguration();

                byte[] key;
                if (legacy)
                {
                    if (value.Length < 32) throw InvalidConfiguration();
                    var material = Encoding.UTF8.GetBytes("melodytrack:pii:" + value);
                    try { key = SHA256.HashData(material); }
                    finally { CryptographicOperations.ZeroMemory(material); }
                }
                else
                {
                    try { key = Convert.FromBase64String(value); }
                    catch (FormatException) { throw InvalidConfiguration(); }
                    if (key.Length != 32)
                    {
                        CryptographicOperations.ZeroMemory(key);
                        throw InvalidConfiguration();
                    }
                }

                keys.Add(version, key);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    internal static PersonalDataKeys FromBase64(IReadOnlyDictionary<string, string> values) => new(values, false);
    internal static PersonalDataKeys FromLegacy(IReadOnlyDictionary<string, string> values) => new(values, true);

    internal static bool ValidVersion(string? version) => !string.IsNullOrEmpty(version)
        && version.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    internal byte[] Get(string version)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return keys.TryGetValue(version, out var key) ? key : throw new CryptographicException("Ключ защищённых данных недоступен.");
    }

    internal static ArgumentException InvalidConfiguration() => new("Некорректная конфигурация ключей защиты данных.");

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        foreach (var key in keys.Values) CryptographicOperations.ZeroMemory(key);
        keys.Clear();
    }
}
