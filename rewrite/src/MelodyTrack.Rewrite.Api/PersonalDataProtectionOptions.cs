namespace MelodyTrack.Rewrite.Api;

/// <summary>Версии и Base64-ключи защиты нового хранилища; значения поступают из секретной конфигурации.</summary>
public sealed class PersonalDataProtectionOptions
{
    /// <summary>Раздел конфигурации нового хранилища.</summary>
    public const string SectionName = "PersonalData";

    /// <summary>Версия ключа для новых записей.</summary>
    public string CurrentKeyVersion { get; set; } = "";

    /// <summary>Текущий и прежние ключи: по 32 случайных байта, представленных в Base64.</summary>
    public Dictionary<string, string> Keys { get; set; } = new(StringComparer.Ordinal);
}
