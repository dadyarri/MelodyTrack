namespace MelodyTrack.Backend.Api.Auth.Responses;

public class RecoveryCodesResponse
{
    public required List<string> Codes { get; set; }
}