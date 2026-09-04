namespace MelodyTrack.Backend.Data.Models;

public class PasswordRestorationRequest : BaseModel
{
    public required string Email { get; set; }
    public required string Token { get; set; }
    public bool WasUsed { get; set; }
    public DateTime ValidUntil { get; set; }
}