namespace MelodyTrack.Common.Api.Auth.Requests;

public class CheckIf2FaEnabledRequest
{
    public required string Email { get; set; }
}