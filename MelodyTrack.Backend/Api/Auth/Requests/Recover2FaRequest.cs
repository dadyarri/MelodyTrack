namespace MelodyTrack.Backend.Api.Auth.Requests;

public class Recover2FaRequest
{
    public required string Email { get; set; }
    public required string RecoveryCode { get; set; }
}