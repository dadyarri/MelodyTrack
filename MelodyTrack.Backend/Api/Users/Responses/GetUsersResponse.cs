namespace MelodyTrack.Backend.Api.Users.Responses;

public class GetUsersResponse
{
    public required List<GetUsersDto> Users { get; set; }
}