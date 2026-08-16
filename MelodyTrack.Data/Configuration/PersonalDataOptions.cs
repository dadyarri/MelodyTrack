using System.ComponentModel.DataAnnotations;

namespace MelodyTrack.Data.Configuration;

public sealed class PersonalDataOptions
{
    public const string SectionName = "PersonalData";

    [Required]
    public string CurrentKeyVersion { get; init; } = "v1";

    [Required]
    [MinLength(32)]
    public required string CurrentKey { get; init; }

    public Dictionary<string, string> Keys { get; init; } = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> BuildKeyRing()
    {
        var result = new Dictionary<string, string>(Keys, StringComparer.Ordinal)
        {
            [CurrentKeyVersion] = CurrentKey
        };
        return result;
    }
}
