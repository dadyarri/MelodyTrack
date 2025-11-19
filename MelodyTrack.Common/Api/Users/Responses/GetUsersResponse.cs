namespace MelodyTrack.Common.Api.Users.Responses;

public class GetUsersResponse
{
    public required List<GetUsersDto> Users { get; set; }
}