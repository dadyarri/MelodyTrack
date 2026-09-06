namespace MelodyTrack.Backend.Api.AliasDictionaries.Responses;

public class AliasDictionaryEntryDto
{
    public required Ulid Id { get; set; }
    public required string Value { get; set; }
    public bool IsEnabled { get; set; }
}

public class GetAliasDictionaryResponse
{
    public List<AliasDictionaryEntryDto> Items { get; set; } = [];
}
