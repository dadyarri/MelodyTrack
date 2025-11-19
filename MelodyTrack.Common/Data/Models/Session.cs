namespace MelodyTrack.Common.Data.Models;

public class Session : BaseModel
{
    public required User User { get; set; }
    public DateTime ValidUntil { get; set; }
    public bool WasRevoked { get; set; }
    public required string DeviceInfo { get; set; }
    public required string RefreshToken { get; set; }
}