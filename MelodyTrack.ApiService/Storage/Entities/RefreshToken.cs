namespace MelodyTrack.ApiService.Storage.Entities;

public class RefreshToken : BaseEntity
{
    public required User User { get; set; }
    public required string Token { get; set; }
    public DateTime IssuedAt { get; set; }
    public DateTime ExpireAt { get; set; }

}
