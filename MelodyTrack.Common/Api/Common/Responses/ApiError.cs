namespace MelodyTrack.Common.Api.Common.Responses;

public class ApiError
{
    public required string Message { get; set; }
    public required string Code { get; set; }
    public string? Field { get; set; }

    public override string ToString()
    {
        if (Field is not null)
        {
            return $"[{Field}: {Message} ({Code})]";
        }
        return $"[{Message} ({Code})]";
    }
}