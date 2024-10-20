namespace MelodyTrack.ApiService.Storage.Entities;

public class RefreshToken : BaseEntity
{
    public required User User { get; set; }
    public required string Token { get; set; }
    public DateTime IssuedAt { get; init; } = DateTime.UtcNow;
    public DateTime ExpireAt { get; init; } = DateTime.UtcNow.AddDays(7);
    public bool Revoked { get; set; }
}
