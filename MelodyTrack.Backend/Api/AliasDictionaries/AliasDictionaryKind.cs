namespace MelodyTrack.Backend.Api.AliasDictionaries;

public enum AliasDictionaryKind
{
    Epithets,
    Creatures,
    BardicTerms
}

public static class AliasDictionaryKindParser
{
    public static AliasDictionaryKind Parse(string value)
    {
        if (Enum.TryParse<AliasDictionaryKind>(value, true, out var dictionary))
        {
            return dictionary;
        }

        throw new BadHttpRequestException("Неизвестный словарь псевдонимов.");
    }
}
