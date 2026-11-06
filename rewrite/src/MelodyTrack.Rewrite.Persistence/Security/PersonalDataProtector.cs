using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace MelodyTrack.Rewrite.Persistence.Security;

/// <summary>Защищает значения AES-256-GCM с привязкой к назначению и владельцу.</summary>
/// <remarks>
/// Формат mtpii:1 содержит версию ключа, случайный 96-битный nonce и 128-битный tag.
/// Контекст и версия ключа аутентифицируются. Открытый текст при чтении не допускается.
/// Экземпляр можно использовать параллельно; освобождайте его после завершения всех операций.
/// </remarks>
public sealed class PersonalDataProtector : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly PersonalDataKeys keys;
    private readonly string currentKeyVersion;
    private bool disposed;

    /// <summary>Загружает независимые 256-битные ключи нового хранилища из Base64.</summary>
    /// <param name="currentKeyVersion">Версия для новых записей, ASCII буквы/цифры, дефис или подчёркивание.</param>
    /// <param name="keysByVersion">Текущий и сохранённые прежние ключи; по 32 случайных байта в Base64.</param>
    /// <exception cref="ArgumentException">Конфигурация неполна или содержит неверные версии/ключи.</exception>
    public PersonalDataProtector(string currentKeyVersion, IReadOnlyDictionary<string, string> keysByVersion)
    {
        ArgumentNullException.ThrowIfNull(keysByVersion);
        if (!PersonalDataKeys.ValidVersion(currentKeyVersion) || !keysByVersion.Keys.Contains(currentKeyVersion, StringComparer.Ordinal))
            throw PersonalDataKeys.InvalidConfiguration();
        keys = PersonalDataKeys.FromBase64(keysByVersion);
        this.currentKeyVersion = currentKeyVersion;
    }

    /// <summary>Шифрует значение без нормализации; null остаётся null, пустая строка шифруется.</summary>
    /// <param name="plaintext">Исходное значение, включая пробелы.</param>
    /// <param name="context">Устойчивое назначение, включающее ID владельца и имя поля.</param>
    /// <returns>Защищённое значение или null.</returns>
    [return: NotNullIfNotNull(nameof(plaintext))]
    public string? Protect(string? plaintext, string context)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        if (plaintext is null) return null;

        byte[] bytes;
        try { bytes = Utf8.GetBytes(plaintext); }
        catch (EncoderFallbackException) { throw new ArgumentException("Значение содержит некорректный Unicode.", nameof(plaintext)); }
        try
        {
            var nonce = RandomNumberGenerator.GetBytes(12);
            var ciphertext = new byte[bytes.Length];
            var tag = new byte[16];
            using var aes = new AesGcm(keys.Get(currentKeyVersion), 16);
            aes.Encrypt(nonce, bytes, ciphertext, tag, AssociatedData(currentKeyVersion, context));
            return string.Join(':', "mtpii", "1", currentKeyVersion, Convert.ToBase64String(nonce),
                Convert.ToBase64String(ciphertext), Convert.ToBase64String(tag));
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }

    /// <summary>Проверяет формат, ключ, контекст и целостность, затем расшифровывает значение.</summary>
    /// <param name="storedValue">Защищённое значение или null.</param>
    /// <param name="context">Тот же контекст, что при записи.</param>
    /// <returns>Исходный текст без изменений или null.</returns>
    /// <exception cref="CryptographicException">Значение непригодно для чтения; исходные данные в ошибку не включаются.</exception>
    [return: NotNullIfNotNull(nameof(storedValue))]
    public string? Unprotect(string? storedValue, string context)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(context);
        if (storedValue is null) return null;

        byte[]? plaintext = null;
        try
        {
            var parts = storedValue.Split(':', 6);
            if (parts.Length != 6 || parts[0] != "mtpii" || parts[1] != "1" || !PersonalDataKeys.ValidVersion(parts[2]))
                throw ReadFailure();
            var nonce = Convert.FromBase64String(parts[3]);
            var ciphertext = Convert.FromBase64String(parts[4]);
            var tag = Convert.FromBase64String(parts[5]);
            if (nonce.Length != 12 || tag.Length != 16) throw ReadFailure();

            plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(keys.Get(parts[2]), 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext, AssociatedData(parts[2], context));
            return Utf8.GetString(plaintext);
        }
        catch (Exception exception) when (exception is FormatException or CryptographicException or DecoderFallbackException)
        {
            throw ReadFailure();
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] AssociatedData(string version, string context) => Utf8.GetBytes("mtpii:1:" + version + ":" + context);
    private static CryptographicException ReadFailure() => new("Не удалось прочитать защищённые данные.");

    /// <summary>Обнуляет загруженные ключи; повторное последовательное освобождение безопасно.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        keys.Dispose();
    }
}
