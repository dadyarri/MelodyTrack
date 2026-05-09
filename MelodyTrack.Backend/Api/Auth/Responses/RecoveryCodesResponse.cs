namespace MelodyTrack.Backend.Api.Auth.Responses;

public class RecoveryCodesResponse
{
    public required List<string> Codes { get; set; }
    public required List<RecoveryCodeDto> AllCodes { get; set; }
}

public class RecoveryCodeDto
{
    public required string Code { get; set; }
    public bool WasUsed { get; set; }
}
