namespace MelodyTrack.Common.Contracts.Auth.Reset;

public class ResetRequest
{
    public string ResetCode { get; set; }
    public string NewPassword { get; set; }
}
