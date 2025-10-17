using MelodyTrack.Backend.Data.Models;

namespace MelodyTrack.Backend.Api.Auth.Responses;

public class GetSessionsResponse
{
    public List<SessionDto> Data { get; set; }
}

public class SessionDto
{
    public Ulid Id { get; set; }
    public string DeviceInfo { get; set; }
}