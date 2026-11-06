using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using MelodyTrack.Rewrite.Persistence.Security;

namespace MelodyTrack.Rewrite.Persistence.Import;

/// <summary>Читает личные данные формата production-референса 1636c2c только для переноса.</summary>
/// <remarks>
/// Читает enc:version:nonce:ciphertext:tag с прежним SHA-256-преобразованием master key.
/// Исходный открытый текст сохраняется без изменений. Повреждённый enc: не считается открытым текстом.
/// Сервис не шифрует данные и не регистрируется в обычном API.
/// </remarks>
public sealed class LegacyPersonalDataReader : IDisposable
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly PersonalDataKeys keys;
    private bool disposed;

    /// <summary>Загружает только ключи исходной установки, включая использованные прежние версии.</summary>
    /// <param name="masterKeysByVersion">Исходные строковые master key по версиям, длина не менее 32 символов.</param>
    public LegacyPersonalDataReader(IReadOnlyDictionary<string, string> masterKeysByVersion)
    {
        keys = PersonalDataKeys.FromLegacy(masterKeysByVersion);
    }

    /// <summary>Восстанавливает значение, не меняя форматирование и не подставляя данные при отказе.</summary>
    /// <param name="storedValue">Точное значение из исходной БД.</param>
    /// <returns>Исходный открытый текст или null.</returns>
    /// <exception cref="CryptographicException">Защищённое значение не прочитано; требуется ручной разбор.</exception>
    [return: NotNullIfNotNull(nameof(storedValue))]
    public string? Read(string? storedValue)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (storedValue is null || !storedValue.StartsWith("enc:", StringComparison.Ordinal)) return storedValue;

        byte[]? plaintext = null;
        try
        {
            var parts = storedValue.Split(':', 5);
            if (parts.Length != 5) throw ReadFailure();
            var nonce = Convert.FromBase64String(parts[2]);
            var ciphertext = Convert.FromBase64String(parts[3]);
            var tag = Convert.FromBase64String(parts[4]);
            if (nonce.Length != 12 || tag.Length != 16) throw ReadFailure();

            plaintext = new byte[ciphertext.Length];
            using var aes = new AesGcm(keys.Get(parts[1]), 16);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);
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

    private static CryptographicException ReadFailure() => new("Не удалось прочитать защищённые данные источника.");

    /// <summary>Обнуляет загруженные ключи после завершения импорта.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        keys.Dispose();
    }
}
