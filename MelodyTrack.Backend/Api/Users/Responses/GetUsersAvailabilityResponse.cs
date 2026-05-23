namespace MelodyTrack.Backend.Api.Users.Responses;

public class GetUsersAvailabilityResponse
{
    public required List<UserAvailabilityResponse> Availabilities { get; set; }
}
