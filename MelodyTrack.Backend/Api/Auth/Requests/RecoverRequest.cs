namespace MelodyTrack.Backend.Api.Auth.Requests;

public class RecoverRequest
{
    public required string Email { get; set; }
    public required string RecoveryCode { get; set; }
}