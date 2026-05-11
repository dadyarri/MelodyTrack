namespace MelodyTrack.Backend.Api.Auth.Responses;

public class GetSessionsResponse
{
    public required List<SessionDto> Data { get; set; }
}

public class SessionDto
{
    public Ulid Id { get; set; }
    public required string DeviceInfo { get; set; }
    public bool IsCurrent { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
}
